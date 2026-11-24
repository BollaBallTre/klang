using System.Runtime.InteropServices;
using Klang.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Klang.Audio;

/// <summary>
/// Leser lyden fra CABLE Output, legger på EQ og spiller den ut på valgt høyttaler.
/// </summary>
public sealed class EqEngine : IDisposable
{
    /// <summary>Hvor mye lyd som kan ligge i kø før vi kaster den eldste, for å holde forsinkelsen nede.</summary>
    private static readonly TimeSpan MaxQueue = TimeSpan.FromMilliseconds(120);
    private const int OutputLatencyMs = 30;
    private const int CaptureBufferMs = 10;

    private readonly object _gate = new();
    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private BufferedWaveProvider? _buffer;
    private Equalizer? _eq;
    private SampleKind _kind;
    private float[] _scratch = Array.Empty<float>();
    private byte[] _bytes = Array.Empty<byte>();
    private double[] _gains = Bands.Flat();
    private bool _enabled = true;
    private bool _stopping;
    private float _inputPeak;
    private long _bytesIn;

    /// <summary>Høyeste nivå (0–1) fra VB-Cable siden forrige kall, og antall byte mottatt. Nullstilles ved lesing.</summary>
    public (float Peak, long Bytes) TakeInputStats()
    {
        var r = (_inputPeak, _bytesIn);
        _inputPeak = 0;
        _bytesIn = 0;
        return r;
    }

    /// <summary>Kalles fra en lydtråd når noe går galt (f.eks. høyttaleren ble koblet fra).</summary>
    public event Action<string>? Faulted;

    public bool IsRunning { get; private set; }
    public string? SpeakerName { get; private set; }
    public int SampleRate { get; private set; }

    /// <summary>Anslått forsinkelse gjennom Klang i millisekunder (kø + utgangsbuffer + opptaksbuffer).</summary>
    public int EstimatedLatencyMs
    {
        get
        {
            var queued = _buffer?.BufferedDuration.TotalMilliseconds ?? 0;
            return (int)Math.Round(queued + OutputLatencyMs + CaptureBufferMs);
        }
    }

    public void SetGains(IReadOnlyList<double> gains)
    {
        _gains = Bands.Normalize(gains);
        _eq?.SetGains(_gains);
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _eq?.SetEnabled(enabled);
    }

    public void Start(MMDevice cableOutput, MMDevice speaker)
    {
        lock (_gate)
        {
            StopCore();
            _stopping = false;
            var capture = new WasapiCapture(cableOutput, useEventSync: true, audioBufferMillisecondsLength: CaptureBufferMs);
            var fmt = capture.WaveFormat;
            _kind = SampleKindOf(fmt);
            var floatFormat = WaveFormat.CreateIeeeFloatWaveFormat(fmt.SampleRate, fmt.Channels);

            _eq = new Equalizer(fmt.SampleRate, fmt.Channels);
            _eq.SetGains(_gains);
            _eq.SetEnabled(_enabled);

            _buffer = new BufferedWaveProvider(floatFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(500),
                DiscardOnBufferOverflow = true,
                ReadFully = true,
            };

            var output = new WasapiOut(speaker, AudioClientShareMode.Shared, useEventSync: true, latency: OutputLatencyMs);
            output.Init(_buffer);
            output.PlaybackStopped += (_, e) => { Log.Info($"Avspilling stoppet: {e.Exception}"); if (!_stopping) Faulted?.Invoke(e.Exception?.Message ?? "Avspillingen stoppet."); };
            capture.DataAvailable += OnData;
            capture.RecordingStopped += (_, e) => { Log.Info($"Opptak stoppet: {e.Exception}"); if (!_stopping) Faulted?.Invoke(e.Exception?.Message ?? "Opptaket fra VB-Cable stoppet."); };

            _capture = capture;
            _output = output;
            SampleRate = fmt.SampleRate;
            SpeakerName = speaker.FriendlyName;

            Log.Info($"Motor: fanger fra '{cableOutput.FriendlyName}' ({fmt}, {_kind}), spiller til '{speaker.FriendlyName}' ({floatFormat}).");
            capture.StartRecording();
            output.Play();
            IsRunning = true;
            Log.Info($"Motor startet. Avspilling: {output.PlaybackState}.");
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var buffer = _buffer;
        var eq = _eq;
        if (buffer is null || eq is null || e.BytesRecorded == 0) return;

        int samples = ToFloat(e.Buffer.AsSpan(0, e.BytesRecorded));
        var span = _scratch.AsSpan(0, samples);
        float peak = 0;
        foreach (var x in span) { var a = Math.Abs(x); if (a > peak) peak = a; }
        if (peak > _inputPeak) _inputPeak = peak;
        _bytesIn += e.BytesRecorded;
        eq.Process(span);

        // Hold køen kort. Klokkene til VB-Cable og høyttaleren går aldri helt likt,
        // så uten dette ville forsinkelsen sakte vokse.
        if (buffer.BufferedDuration > MaxQueue) buffer.ClearBuffer();

        var bytes = MemoryMarshal.AsBytes(span);
        if (_bytes.Length < bytes.Length) _bytes = new byte[bytes.Length];
        bytes.CopyTo(_bytes);
        buffer.AddSamples(_bytes, 0, bytes.Length);
    }

    private int ToFloat(ReadOnlySpan<byte> src)
    {
        int count = _kind switch
        {
            SampleKind.Float32 or SampleKind.Int32 => src.Length / 4,
            SampleKind.Int24 => src.Length / 3,
            _ => src.Length / 2,
        };
        if (_scratch.Length < count) _scratch = new float[count];
        var dst = _scratch.AsSpan(0, count);
        switch (_kind)
        {
            case SampleKind.Float32:
                MemoryMarshal.Cast<byte, float>(src[..(count * 4)]).CopyTo(dst);
                break;
            case SampleKind.Int32:
                var i32 = MemoryMarshal.Cast<byte, int>(src[..(count * 4)]);
                for (int i = 0; i < count; i++) dst[i] = i32[i] / 2147483648f;
                break;
            case SampleKind.Int24:
                for (int i = 0, j = 0; i < count; i++, j += 3)
                    dst[i] = ((src[j] << 8 | src[j + 1] << 16 | src[j + 2] << 24) >> 8) / 8388608f;
                break;
            default:
                var i16 = MemoryMarshal.Cast<byte, short>(src[..(count * 2)]);
                for (int i = 0; i < count; i++) dst[i] = i16[i] / 32768f;
                break;
        }
        return count;
    }

    private enum SampleKind { Float32, Int16, Int24, Int32 }

    // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private static SampleKind SampleKindOf(WaveFormat f)
    {
        bool isFloat = f.Encoding == WaveFormatEncoding.IeeeFloat
            || (f is WaveFormatExtensible x && x.SubFormat == IeeeFloatSubFormat);
        if (isFloat && f.BitsPerSample == 32) return SampleKind.Float32;
        return f.BitsPerSample switch
        {
            16 => SampleKind.Int16,
            24 => SampleKind.Int24,
            32 => SampleKind.Int32,
            _ => throw new NotSupportedException($"Lydformatet til VB-Cable støttes ikke ({f}).")
        };
    }

    public void Stop()
    {
        lock (_gate) StopCore();
    }

    private void StopCore()
    {
        _stopping = true;
        IsRunning = false;
        try { _capture?.StopRecording(); } catch { }
        try { _output?.Stop(); } catch { }
        _capture?.Dispose();
        _output?.Dispose();
        _capture = null;
        _output = null;
        _buffer = null;
        _eq = null;
    }

    public void Dispose() => Stop();
}
