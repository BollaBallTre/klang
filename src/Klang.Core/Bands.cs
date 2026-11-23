namespace Klang.Core;

/// <summary>De ti faste frekvensbåndene (oktavavstand) og grensene for forsterkning.</summary>
public static class Bands
{
    public const int Count = 10;
    public const double MinGainDb = -12.0;
    public const double MaxGainDb = 12.0;
    public const double GainStepDb = 0.5;

    /// <summary>Q for oktavbredde. Gir jevne overganger mellom nabobånd.</summary>
    public const double Q = 1.41;

    public static readonly double[] Frequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    public static readonly string[] Labels = { "31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };

    public static double[] Flat() => new double[Count];

    /// <summary>Klemmer til lovlig område og runder til nærmeste halve dB.</summary>
    public static double Clamp(double gainDb)
    {
        if (double.IsNaN(gainDb)) return 0;
        var c = Math.Clamp(gainDb, MinGainDb, MaxGainDb);
        return Math.Round(c / GainStepDb) * GainStepDb;
    }

    public static double[] Normalize(IReadOnlyList<double>? gains)
    {
        var result = new double[Count];
        if (gains is null) return result;
        for (int i = 0; i < Count && i < gains.Count; i++) result[i] = Clamp(gains[i]);
        return result;
    }
}
