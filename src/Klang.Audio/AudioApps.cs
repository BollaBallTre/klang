using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Klang.Audio;

/// <summary>Et program som har lyd i Windows. Ett program kan ha flere prosesser (f.eks. Chrome).</summary>
public sealed record AudioApp(string Key, string DisplayName, IReadOnlyList<int> ProcessIds, bool IsPlaying)
{
    public override string ToString() => DisplayName;
}

public static class AudioApps
{
    /// <summary>Lister programmer med en lydøkt på en av avspillingsenhetene, inkludert VB-Cable.</summary>
    public static IReadOnlyList<AudioApp> List()
    {
        int self = Environment.ProcessId;
        var found = new Dictionary<string, (string Display, HashSet<int> Pids, bool Playing)>(StringComparer.OrdinalIgnoreCase);
        using var e = new MMDeviceEnumerator();
        foreach (var device in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            SessionCollection sessions;
            try
            {
                var mgr = device.AudioSessionManager;
                mgr.RefreshSessions();
                sessions = mgr.Sessions;
            }
            catch { continue; }

            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (s.IsSystemSoundsSession) continue;
                int pid = (int)s.GetProcessID;
                if (pid == 0 || pid == self) continue;
                if (!TryDescribe(pid, out var key, out var display)) continue;
                bool playing = s.State == AudioSessionState.AudioSessionStateActive;
                if (!found.TryGetValue(key, out var entry)) entry = (display, new HashSet<int>(), false);
                entry.Pids.Add(pid);
                found[key] = (entry.Display, entry.Pids, entry.Playing || playing);
            }
        }
        return found
            .Select(kv => new AudioApp(kv.Key, kv.Value.Display, kv.Value.Pids.ToList(), kv.Value.Playing))
            .OrderByDescending(a => a.IsPlaying)
            .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool TryDescribe(int pid, out string key, out string display)
    {
        key = display = "";
        try
        {
            using var p = Process.GetProcessById(pid);
            key = p.ProcessName;
            display = key;
            try
            {
                var desc = p.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) display = desc!;
            }
            catch { /* Ingen tilgang til modulinfo for noen prosesser. Bruk prosessnavnet. */ }
            return true;
        }
        catch { return false; }
    }
}
