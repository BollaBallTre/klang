using NAudio.CoreAudioApi;

namespace Klang.Audio;

public sealed record DeviceInfo(string Id, string Name);

/// <summary>Finner VB-Cable og de ekte høyttalerne.</summary>
public static class AudioDevices
{
    private const string CableInputName = "CABLE Input";
    private const string CableOutputName = "CABLE Output";

    private static bool IsCable(MMDevice d) =>
        d.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) &&
        d.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)
        || d.FriendlyName.StartsWith(CableInputName, StringComparison.OrdinalIgnoreCase)
        || d.FriendlyName.StartsWith(CableOutputName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Avspillingsenheten programmet skal sende lyden til.</summary>
    public static MMDevice? FindCableInput()
    {
        using var e = new MMDeviceEnumerator();
        return e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.StartsWith(CableInputName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Opptaksenheten Klang leser lyden fra.</summary>
    public static MMDevice? FindCableOutput()
    {
        using var e = new MMDeviceEnumerator();
        return e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.StartsWith(CableOutputName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Alle aktive avspillingsenheter unntatt VB-Cable.</summary>
    public static IReadOnlyList<DeviceInfo> Speakers()
    {
        using var e = new MMDeviceEnumerator();
        return e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Where(d => !IsCable(d))
            .Select(d => new DeviceInfo(d.ID, d.FriendlyName))
            .ToList();
    }

    /// <summary>True når Windows sin standard avspillingsenhet er VB-Cable. Da forsvinner all lyd inn i kabelen.</summary>
    public static bool DefaultOutputIsCable()
    {
        using var e = new MMDeviceEnumerator();
        if (!e.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return false;
        return IsCable(e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia));
    }

    /// <summary>
    /// Høyttaleren lyden skal ut på: den lagrede hvis den finnes, ellers Windows-standard,
    /// ellers første enhet som ikke er VB-Cable.
    /// </summary>
    public static MMDevice? ResolveSpeaker(string? preferredId)
    {
        using var e = new MMDeviceEnumerator();
        var active = e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Where(d => !IsCable(d)).ToList();
        if (preferredId is not null)
        {
            var match = active.FirstOrDefault(d => d.ID == preferredId);
            if (match is not null) return match;
        }
        if (e.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            var def = e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (!IsCable(def)) return def;
        }
        return active.FirstOrDefault();
    }
}
