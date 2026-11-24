namespace Klang.Core;

/// <summary>
/// Ti peaking-filtre i serie, pluss automatisk volumvern.
/// Trådsikker: UI-tråden kaller <see cref="SetGains"/>, lydtråden kaller <see cref="Process"/>.
/// </summary>
public sealed class Equalizer
{
    private sealed record State(BiquadCoefficients[] Coefficients, float Preamp, bool Enabled);

    private volatile State _state;
    private readonly int _channels;
    private readonly double _sampleRate;
    // Filtertilstand per kanal og bånd (transponert direkte form II).
    private readonly double[,] _z1;
    private readonly double[,] _z2;
    private double[] _gains = Bands.Flat();
    private bool _enabled = true;
    private double _preampDb = Bands.DefaultPreampDb;

    public Equalizer(double sampleRate, int channels)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        _sampleRate = sampleRate;
        _channels = channels;
        _z1 = new double[channels, Bands.Count];
        _z2 = new double[channels, Bands.Count];
        _state = Build();
    }

    public int Channels => _channels;
    public double SampleRate => _sampleRate;
    /// <summary>Volumvernet i dB (negativt tall, −10 til −3).</summary>
    public double PreampDb => _preampDb;
    public bool Enabled => _enabled;
    public IReadOnlyList<double> Gains => _gains;

    public void SetGains(IReadOnlyList<double> gains)
    {
        _gains = Bands.Normalize(gains);
        _state = Build();
    }

    public void SetPreamp(double preampDb)
    {
        _preampDb = Bands.ClampPreamp(preampDb);
        _state = Build();
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _state = Build();
    }

    private State Build()
    {
        var coeffs = new BiquadCoefficients[Bands.Count];
        for (int i = 0; i < Bands.Count; i++)
            coeffs[i] = BiquadCoefficients.Peaking(_sampleRate, Bands.Frequencies[i], _gains[i], Bands.Q);
        float preamp = (float)Math.Pow(10, _preampDb / 20);
        return new State(coeffs, preamp, _enabled);
    }

    /// <summary>Behandler sammenflettede (interleaved) float-samples på stedet.</summary>
    public void Process(Span<float> samples)
    {
        var s = _state;
        if (!s.Enabled) return;
        int frames = samples.Length / _channels;
        for (int b = 0; b < Bands.Count; b++)
        {
            var c = s.Coefficients[b];
            if (c.IsIdentity) continue;
            for (int ch = 0; ch < _channels; ch++)
            {
                double z1 = _z1[ch, b], z2 = _z2[ch, b];
                for (int f = 0, idx = ch; f < frames; f++, idx += _channels)
                {
                    double x = samples[idx];
                    double y = c.B0 * x + z1;
                    z1 = c.B1 * x - c.A1 * y + z2;
                    z2 = c.B2 * x - c.A2 * y;
                    samples[idx] = (float)y;
                }
                // Unngå denormaliserte tall i stillhet.
                _z1[ch, b] = Math.Abs(z1) < 1e-20 ? 0 : z1;
                _z2[ch, b] = Math.Abs(z2) < 1e-20 ? 0 : z2;
            }
        }
        if (s.Preamp != 1f)
            for (int i = 0; i < samples.Length; i++) samples[i] *= s.Preamp;
    }

    public void Reset()
    {
        Array.Clear(_z1);
        Array.Clear(_z2);
    }
}
