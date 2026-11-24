using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klang.Core;

/// <summary>Det som lagres i settings.json.</summary>
public sealed class KlangSettings
{
    public int Version { get; set; } = 1;
    public List<CustomPreset> CustomPresets { get; set; } = new();
    /// <summary>Siste valg per program, nøkkel er prosessnavnet (f.eks. «Spotify»).</summary>
    public Dictionary<string, AppEqState> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? LastApp { get; set; }
    /// <summary>Id til høyttaleren lyden skal ut på. Tom betyr Windows-standard (unntatt VB-Cable).</summary>
    public string? OutputDeviceId { get; set; }
    /// <summary>Programmer Klang har flyttet til VB-Cable, så de kan flyttes tilbake ved avslutning eller krasj.</summary>
    public List<string> RoutedApps { get; set; } = new();
}

public sealed class CustomPreset
{
    public string Name { get; set; } = "";
    public double[] Gains { get; set; } = Bands.Flat();
}

public sealed class AppEqState
{
    /// <summary>Navnet på innstillingen som sist ble valgt (ferdig, egen eller «Flat»).</summary>
    public string PresetName { get; set; } = Presets.FlatName;
    /// <summary>Bryterverdiene, som kan være endret etter at innstillingen ble valgt.</summary>
    public double[] Gains { get; set; } = Bands.Flat();
    public bool Enabled { get; set; } = true;
    /// <summary>Volumvern i dB, −10 til −3.</summary>
    public double PreampDb { get; set; } = Bands.DefaultPreampDb;
}

public enum SaveNameProblem { None, Empty, TooLong, ReservedName }

/// <summary>Leser og skriver innstillinger som JSON. Skriver først til en midlertidig fil så en krasj ikke ødelegger filen.</summary>
public sealed class SettingsStore
{
    public const int MaxNameLength = 32;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string FilePath { get; }

    public SettingsStore(string filePath) => FilePath = filePath;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klang", "settings.json");

    public KlangSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new KlangSettings();
            var s = JsonSerializer.Deserialize<KlangSettings>(File.ReadAllText(FilePath), Json) ?? new KlangSettings();
            return Sanitize(s);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Ødelagt fil: ta vare på den og start på nytt.
            try { File.Copy(FilePath, FilePath + ".broken", overwrite: true); } catch { }
            return new KlangSettings();
        }
    }

    public void Save(KlangSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public static SaveNameProblem ValidateName(string? name)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0) return SaveNameProblem.Empty;
        if (n.Length > MaxNameLength) return SaveNameProblem.TooLong;
        if (Presets.IsReservedName(n)) return SaveNameProblem.ReservedName;
        return SaveNameProblem.None;
    }

    /// <summary>Lagrer eller overskriver en egen innstilling med samme navn (store/små bokstaver likegyldig).</summary>
    public static CustomPreset Upsert(KlangSettings s, string name, IReadOnlyList<double> gains)
    {
        if (ValidateName(name) != SaveNameProblem.None) throw new ArgumentException("Ugyldig navn", nameof(name));
        name = name.Trim();
        var existing = s.CustomPresets.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new CustomPreset { Name = name };
            s.CustomPresets.Add(existing);
        }
        existing.Gains = Bands.Normalize(gains);
        return existing;
    }

    public static bool Delete(KlangSettings s, string name)
    {
        int removed = s.CustomPresets.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return false;
        // Programmer som brukte den slettede innstillingen beholder bryterne, men står ikke lenger på et navn.
        foreach (var app in s.Apps.Values)
            if (string.Equals(app.PresetName, name, StringComparison.OrdinalIgnoreCase)) app.PresetName = "";
        return true;
    }

    /// <summary>Finner bryterverdiene til en innstilling, enten ferdig, egen eller Flat.</summary>
    public static double[]? GainsFor(KlangSettings s, string name)
    {
        var builtIn = Presets.FindBuiltIn(name);
        if (builtIn is not null) return builtIn.Gains.ToArray();
        var c = s.CustomPresets.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return c?.Gains.ToArray();
    }

    private static KlangSettings Sanitize(KlangSettings s)
    {
        s.CustomPresets ??= new();
        s.CustomPresets = s.CustomPresets
            .Where(c => ValidateName(c.Name) == SaveNameProblem.None)
            .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new CustomPreset { Name = g.Key, Gains = Bands.Normalize(g.Last().Gains) })
            .ToList();
        var apps = new Dictionary<string, AppEqState>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in s.Apps ?? new())
            if (!string.IsNullOrWhiteSpace(k) && v is not null)
            {
                v.Gains = Bands.Normalize(v.Gains);
                v.PreampDb = Bands.ClampPreamp(v.PreampDb);
                v.PresetName ??= "";
                apps[k] = v;
            }
        s.Apps = apps;
        s.RoutedApps ??= new();
        return s;
    }
}
