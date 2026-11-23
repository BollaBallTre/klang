namespace Klang.Core;

public sealed record Preset(string Name, double[] Gains, bool IsBuiltIn)
{
    public Preset WithGains(IReadOnlyList<double> gains) => this with { Gains = Bands.Normalize(gains) };
}

public static class Presets
{
    public const string FlatName = "Flat";

    /// <summary>De ti ferdige forhåndsinnstillingene. Flat er en tilbakestilling og er ikke med her.</summary>
    public static readonly IReadOnlyList<Preset> BuiltIn = new[]
    {
        P("Bassforsterker",    6, 5, 4, 2, 0, 0, 0, 0, 0, 0),
        P("Kraftig bass",      9, 8, 6, 3, 1, 0, 0, 0, 0, 0),
        P("Diskantforsterker", 0, 0, 0, 0, 0, 1, 2, 4, 5, 6),
        P("Bass og diskant",   6, 5, 3, 0, -1, -1, 0, 3, 5, 6),
        P("Tale og podkast",  -6, -4, -2, 0, 2, 3, 4, 3, 1, -1),
        P("Vokal",            -2, -1, 0, 1, 3, 4, 3, 1, 0, -1),
        P("Rock",              5, 4, 2, -1, -2, -1, 1, 3, 4, 4),
        P("Pop",              -1, 1, 3, 4, 3, 0, -1, -1, 1, 2),
        P("Elektronisk",       6, 5, 2, 0, -2, 0, 1, 2, 4, 5),
        P("Klassisk",          3, 2, 1, 0, 0, 0, -1, 1, 2, 3),
    };

    public static Preset Flat { get; } = new(FlatName, Bands.Flat(), true);

    public static Preset? FindBuiltIn(string name) =>
        BuiltIn.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? (string.Equals(name, FlatName, StringComparison.OrdinalIgnoreCase) ? Flat : null);

    public static bool IsReservedName(string name) => FindBuiltIn(name.Trim()) is not null;

    private static Preset P(string name, params double[] gains) => new(name, gains, true);
}
