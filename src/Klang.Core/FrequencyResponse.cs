namespace Klang.Core;

/// <summary>Regner ut den samlede kurven for ti bånd. Brukes både til å tegne kurven og til volumvernet.</summary>
public static class FrequencyResponse
{
    public const double ReferenceSampleRate = 48000;

    public static double At(IReadOnlyList<double> gains, double frequency, double sampleRate = ReferenceSampleRate)
    {
        double db = 0;
        for (int i = 0; i < Bands.Count; i++)
            db += BiquadCoefficients.Peaking(sampleRate, Bands.Frequencies[i], gains[i], Bands.Q).MagnitudeDb(sampleRate, frequency);
        return db;
    }

    /// <summary>Punkter logaritmisk fordelt fra 20 Hz til 20 kHz.</summary>
    public static (double Frequency, double Db)[] Curve(IReadOnlyList<double> gains, int points = 200)
    {
        var result = new (double, double)[points + 1];
        for (int i = 0; i <= points; i++)
        {
            double f = 20 * Math.Pow(1000, (double)i / points);
            result[i] = (f, At(gains, f));
        }
        return result;
    }

    public static double Peak(IReadOnlyList<double> gains)
    {
        double peak = double.MinValue;
        foreach (var (_, db) in Curve(gains)) peak = Math.Max(peak, db);
        return peak;
    }

    /// <summary>
    /// Volumvern: hvor mye hele signalet senkes (positivt tall i dB) så den høyeste toppen
    /// ikke går over 0 dB. Rundes opp til nærmeste halve dB.
    /// </summary>
    public static double AutoPreampCut(IReadOnlyList<double> gains)
    {
        double peak = Peak(gains);
        if (peak <= 0) return 0;
        return Math.Ceiling(peak * 2) / 2;
    }

    /// <summary>True når den høyeste toppen pluss volumvernet går over 0 dB, så lyden kan forvrenges.</summary>
    public static bool MayClip(IReadOnlyList<double> gains, double preampDb) => Peak(gains) + preampDb > 0.05;
}
