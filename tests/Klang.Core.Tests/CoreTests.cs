using Klang.Core;
using Xunit;

namespace Klang.Core.Tests;

public class BiquadTests
{
    [Theory]
    [InlineData(31, 6)]
    [InlineData(1000, -6)]
    [InlineData(16000, 12)]
    public void Peaking_HasRequestedGainAtCentre(double f0, double gain)
    {
        var c = BiquadCoefficients.Peaking(48000, f0, gain, Bands.Q);
        Assert.InRange(c.MagnitudeDb(48000, f0), gain - 0.05, gain + 0.05);
    }

    [Fact]
    public void ZeroGain_IsIdentity() => Assert.True(BiquadCoefficients.Peaking(48000, 1000, 0, Bands.Q).IsIdentity);

    [Fact]
    public void Peaking_FarAway_IsNearZero()
    {
        var c = BiquadCoefficients.Peaking(48000, 62, 9, Bands.Q);
        Assert.InRange(c.MagnitudeDb(48000, 8000), -0.1, 0.1);
    }
}

public class EqualizerTests
{
    private static float[] Sine(double freq, int frames, int channels, double rate = 48000, float amp = 0.25f)
    {
        var buf = new float[frames * channels];
        for (int i = 0; i < frames; i++)
            for (int c = 0; c < channels; c++)
                buf[i * channels + c] = amp * (float)Math.Sin(2 * Math.PI * freq * i / rate);
        return buf;
    }

    private static double RmsDb(ReadOnlySpan<float> s)
    {
        double sum = 0; foreach (var x in s) sum += x * x;
        return 10 * Math.Log10(sum / s.Length);
    }

    [Fact]
    public void Flat_LeavesSignalUntouched()
    {
        var eq = new Equalizer(48000, 2);
        var input = Sine(440, 4800, 2);
        var output = input.ToArray();
        eq.Process(output);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Disabled_LeavesSignalUntouched()
    {
        var eq = new Equalizer(48000, 2);
        eq.SetGains(Presets.BuiltIn[1].Gains);
        eq.SetEnabled(false);
        var input = Sine(62, 4800, 2);
        var output = input.ToArray();
        eq.Process(output);
        Assert.Equal(input, output);
    }

    [Fact]
    public void BassBoost_RaisesBassMoreThanTreble()
    {
        var eq = new Equalizer(48000, 1);
        eq.SetGains(Presets.FindBuiltIn("Kraftig bass")!.Gains);
        var bass = Sine(62, 48000, 1);
        eq.Process(bass);
        eq.Reset();
        var treble = Sine(8000, 48000, 1);
        eq.Process(treble);
        // Hopp over innsvingning.
        double bassDb = RmsDb(bass.AsSpan(24000));
        double trebleDb = RmsDb(treble.AsSpan(24000));
        Assert.True(bassDb - trebleDb > 6, $"bass {bassDb:F1} dB, treble {trebleDb:F1} dB");
    }

    [Fact]
    public void Preamp_KeepsPeakAtOrBelowZeroDb()
    {
        foreach (var p in Presets.BuiltIn)
        {
            var cut = FrequencyResponse.AutoPreampCut(p.Gains);
            Assert.True(FrequencyResponse.Peak(p.Gains) - cut <= 0.001, p.Name);
        }
    }

    [Fact]
    public void Processing_DoesNotClipFullScaleSine()
    {
        var eq = new Equalizer(48000, 1);
        eq.SetGains(Presets.FindBuiltIn("Kraftig bass")!.Gains);
        var s = Sine(50, 48000, 1, amp: 1f);
        eq.Process(s);
        Assert.True(s.Skip(24000).Max(Math.Abs) <= 1.05f);
    }
}

public class PresetTests
{
    [Fact]
    public void ThereAreTenBuiltInPresetsWithUniqueNames()
    {
        Assert.Equal(10, Presets.BuiltIn.Count);
        Assert.Equal(10, Presets.BuiltIn.Select(p => p.Name).Distinct().Count());
        Assert.All(Presets.BuiltIn, p => Assert.Equal(Bands.Count, p.Gains.Length));
        Assert.All(Presets.BuiltIn.SelectMany(p => p.Gains), g => Assert.InRange(g, Bands.MinGainDb, Bands.MaxGainDb));
    }

    [Fact]
    public void IncludesBassAndTreble()
    {
        Assert.NotNull(Presets.FindBuiltIn("bassforsterker"));
        Assert.NotNull(Presets.FindBuiltIn("Diskantforsterker"));
    }

    [Theory]
    [InlineData(13.2, 12)]
    [InlineData(-40, -12)]
    [InlineData(3.3, 3.5)]
    [InlineData(double.NaN, 0)]
    public void Clamp_Works(double input, double expected) => Assert.Equal(expected, Bands.Clamp(input));
}

public class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klang-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void MissingFile_GivesDefaults() => Assert.Empty(new SettingsStore(FilePath).Load().CustomPresets);

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var store = new SettingsStore(FilePath);
        var s = new KlangSettings();
        SettingsStore.Upsert(s, "  Kveldsfilm ", new double[] { 3, 2, 1, 0, 0, 0, 0, 1, 2, 3 });
        s.Apps["Spotify"] = new AppEqState { PresetName = "Kveldsfilm", Gains = new double[] { 3, 2, 1, 0, 0, 0, 0, 1, 2, 3 }, Enabled = false };
        s.LastApp = "Spotify";
        s.RoutedApps.Add("Spotify");
        store.Save(s);

        var loaded = store.Load();
        Assert.Equal("Kveldsfilm", Assert.Single(loaded.CustomPresets).Name);
        Assert.Equal(3, loaded.CustomPresets[0].Gains[0]);
        Assert.False(loaded.Apps["spotify"].Enabled);
        Assert.Equal("Spotify", loaded.LastApp);
        Assert.Equal(new[] { "Spotify" }, loaded.RoutedApps);
    }

    [Fact]
    public void Upsert_OverwritesSameNameIgnoringCase()
    {
        var s = new KlangSettings();
        SettingsStore.Upsert(s, "Min", Bands.Flat());
        SettingsStore.Upsert(s, "MIN", new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 });
        Assert.Single(s.CustomPresets);
        Assert.Equal(1, s.CustomPresets[0].Gains[5]);
    }

    [Theory]
    [InlineData("", SaveNameProblem.Empty)]
    [InlineData("   ", SaveNameProblem.Empty)]
    [InlineData("rock", SaveNameProblem.ReservedName)]
    [InlineData("Flat", SaveNameProblem.ReservedName)]
    [InlineData("Kveldsfilm", SaveNameProblem.None)]
    [InlineData("123456789012345678901234567890123", SaveNameProblem.TooLong)]
    public void ValidateName_Works(string name, SaveNameProblem expected) => Assert.Equal(expected, SettingsStore.ValidateName(name));

    [Fact]
    public void Delete_ClearsPresetNameOnAppsButKeepsGains()
    {
        var s = new KlangSettings();
        SettingsStore.Upsert(s, "Min", new double[] { 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 });
        s.Apps["Chrome"] = new AppEqState { PresetName = "Min", Gains = new double[] { 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 } };
        Assert.True(SettingsStore.Delete(s, "min"));
        Assert.Empty(s.CustomPresets);
        Assert.Equal("", s.Apps["Chrome"].PresetName);
        Assert.Equal(2, s.Apps["Chrome"].Gains[0]);
    }

    [Fact]
    public void BrokenFile_IsBackedUpAndDefaultsReturned()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ dette er ikke json");
        var loaded = new SettingsStore(FilePath).Load();
        Assert.Empty(loaded.CustomPresets);
        Assert.True(File.Exists(FilePath + ".broken"));
    }

    [Fact]
    public void Load_CleansBadValues()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"customPresets":[{"name":"Rock","gains":[1]},{"name":"Ok","gains":[99,1]}]}""");
        var loaded = new SettingsStore(FilePath).Load();
        var ok = Assert.Single(loaded.CustomPresets);
        Assert.Equal("Ok", ok.Name);
        Assert.Equal(12, ok.Gains[0]);
        Assert.Equal(Bands.Count, ok.Gains.Length);
    }

    [Fact]
    public void GainsFor_FindsBuiltInCustomAndFlat()
    {
        var s = new KlangSettings();
        SettingsStore.Upsert(s, "Min", new double[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        Assert.Equal(9, SettingsStore.GainsFor(s, "Kraftig bass")![0]);
        Assert.Equal(1, SettingsStore.GainsFor(s, "min")![0]);
        Assert.Equal(0, SettingsStore.GainsFor(s, "Flat")![0]);
        Assert.Null(SettingsStore.GainsFor(s, "Finnes ikke"));
    }
}
