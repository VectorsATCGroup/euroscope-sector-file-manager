namespace Vectors.EuroScopeUpdater.Core.Model;

/// <summary>
/// A VATSIM division served by the updater. Modeled generically so future divisions
/// (other than <c>SBXX</c> / VATSIM Brasil) can be added without structural change,
/// but only VATSIM Brasil ships in this version.
/// </summary>
public sealed record Division(string Id, string Name, IReadOnlyList<Fir> Firs)
{
    /// <summary>The single division shipped in this version.</summary>
    public static Division VatsimBrasil { get; } = new(
        Id: "SBXX",
        Name: "VATSIM Brasil",
        Firs: new[]
        {
            new Fir("SBAO", "Atlântico FIR"),
            new Fir("SBAZ", "Amazônica FIR"),
            new Fir("SBBS", "Brasília FIR"),
            new Fir("SBCW", "Curitiba FIR"),
            new Fir("SBRE", "Recife FIR"),
        });
}

/// <summary>
/// A Flight Information Region. <see cref="Name"/> is a descriptive label for the UI only;
/// <see cref="Code"/> (e.g. <c>SBRE</c>) is the authoritative identifier used for files.
/// </summary>
public sealed record Fir(string Code, string Name);
