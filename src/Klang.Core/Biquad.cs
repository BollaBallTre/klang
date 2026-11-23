namespace Klang.Core;

/// <summary>Koeffisienter for et andreordens filter (RBJ «Audio EQ Cookbook»), normalisert så a0 = 1.</summary>
public readonly record struct BiquadCoefficients(double B0, double B1, double B2, double A1, double A2)
{
    public static readonly BiquadCoefficients Identity = new(1, 0, 0, 0, 0);

    public bool IsIdentity => B0 == 1 && B1 == 0 && B2 == 0 && A1 == 0 && A2 == 0;

    /// <summary>Peaking-filter: løfter eller senker rundt <paramref name="frequency"/>.</summary>
    public static BiquadCoefficients Peaking(double sampleRate, double frequency, double gainDb, double q)
    {
        if (gainDb == 0 || frequency <= 0 || frequency >= sampleRate / 2) return Identity;
        double a = Math.Pow(10, gainDb / 40);
        double w0 = 2 * Math.PI * frequency / sampleRate;
        double alpha = Math.Sin(w0) / (2 * q);
        double cos = Math.Cos(w0);
        double a0 = 1 + alpha / a;
        return new BiquadCoefficients(
            (1 + alpha * a) / a0,
            (-2 * cos) / a0,
            (1 - alpha * a) / a0,
            (-2 * cos) / a0,
            (1 - alpha / a) / a0);
    }

    /// <summary>Forsterkning i dB ved frekvensen <paramref name="frequency"/>.</summary>
    public double MagnitudeDb(double sampleRate, double frequency)
    {
        if (IsIdentity) return 0;
        double w = 2 * Math.PI * frequency / sampleRate;
        double c1 = Math.Cos(w), s1 = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
        double nr = B0 + B1 * c1 + B2 * c2, ni = -(B1 * s1 + B2 * s2);
        double dr = 1 + A1 * c1 + A2 * c2, di = -(A1 * s1 + A2 * s2);
        return 10 * Math.Log10((nr * nr + ni * ni) / (dr * dr + di * di));
    }
}
