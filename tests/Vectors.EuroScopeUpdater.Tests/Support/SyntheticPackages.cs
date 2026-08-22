using Vectors.EuroScopeUpdater.Core.Model;

namespace Vectors.EuroScopeUpdater.Tests.Support;

/// <summary>
/// Builds SYNTHETIC package content directories that mirror the real AeroNav structure
/// (see <c>docs/package-analysis.md</c>) without using any real AeroNav data. The install package is a
/// full FIR; the update package is the observed subset (omits Settings/RADAR, Settings/SOLO,
/// Plugins/*.dll and the .prf files). Content is tagged with the cycle so tests can prove which files
/// changed and which were preserved.
/// </summary>
public static class SyntheticPackages
{
    /// <summary>
    /// Versioned sector base name, e.g. SBRE-SBRE_20260810143935-260801-0001. <paramref name="cycleRevision"/>
    /// is AeroNav's within-cycle re-issue (the RR pair: 260801 → 260802) and <paramref name="packageRevision"/>
    /// the trailing group.
    /// </summary>
    public static string SectorBase(string fir, string cycle, int cycleRevision = 1, int packageRevision = 1) =>
        $"{fir}-{fir}_2026{cycle[2..]}10143935-{cycle}{cycleRevision:D2}-{packageRevision:D4}";

    public static string PackageFileName(string fir, PackageType type, string cycle, int cycleRevision = 1, int packageRevision = 1) =>
        $"{fir}-{(type == PackageType.Install ? "Install" : "Update")}-Package_2026{cycle[2..]}10143935-{cycle}{cycleRevision:D2}-{packageRevision:D4}.7z";

    /// <summary>Content tag: "2608" for a cycle's first issue, "2608/2" for a re-issue, so tests can prove which files changed.</summary>
    private static string Tag(string cycle, int cycleRevision) => cycleRevision == 1 ? cycle : $"{cycle}/{cycleRevision}";

    /// <summary>Create a full install content tree under <paramref name="destDir"/>.</summary>
    public static void BuildInstall(string destDir, string fir, string cycle, int cycleRevision = 1)
    {
        fir = fir.ToUpperInvariant();
        var lower = fir.ToLowerInvariant();
        var sct = SectorBase(fir, cycle, cycleRevision);
        cycle = Tag(cycle, cycleRevision);

        // Root-level versioned sector files + profiles + copyright.
        Write(destDir, $"{sct}.sct", $"SECTOR {fir} {cycle}");
        Write(destDir, $"{sct}.ese", $"ESE {fir} {cycle}");
        Write(destDir, $"{lower}_radar.prf", Prf(fir, sct, "RADAR"));
        Write(destDir, $"{lower}_solo.prf", Prf(fir, sct, "SOLO"));
        Write(destDir, "aeronav_copyright.txt", "SYNTHETIC copyright (not AeroNav data)");

        WriteNested(destDir, fir, cycle, includePersonalizationAndPlugins: true);
    }

    /// <summary>Create an update content tree (the observed subset) under <paramref name="destDir"/>.</summary>
    public static void BuildUpdate(string destDir, string fir, string cycle, int cycleRevision = 1)
    {
        fir = fir.ToUpperInvariant();
        var sct = SectorBase(fir, cycle, cycleRevision);
        cycle = Tag(cycle, cycleRevision);

        // Update ships new versioned sector files + copyright, but NO .prf.
        Write(destDir, $"{sct}.sct", $"SECTOR {fir} {cycle}");
        Write(destDir, $"{sct}.ese", $"ESE {fir} {cycle}");
        Write(destDir, "aeronav_copyright.txt", "SYNTHETIC copyright (not AeroNav data)");

        WriteNested(destDir, fir, cycle, includePersonalizationAndPlugins: false);
    }

    private static void WriteNested(string destDir, string fir, string cycle, bool includePersonalizationAndPlugins)
    {
        // AIRAC data (refreshed each cycle).
        Write(destDir, $"{fir}/Alias/alias.txt", $"alias {cycle}");
        Write(destDir, $"{fir}/NavData/airway.txt", $"airway {cycle}");
        Write(destDir, $"{fir}/NavData/isec.txt", $"isec {cycle}");
        Write(destDir, $"{fir}/ICAO/ICAO_Airports.txt", $"airports {cycle}");
        Write(destDir, $"{fir}/ASR/CTR/{fir}_CTR.asr", $"ctr asr {cycle}");
        Write(destDir, $"{fir}/aeronav_copyright.txt", "SYNTHETIC copyright");

        // Loose Settings files ARE included in updates (refreshed).
        Write(destDir, $"{fir}/Settings/AircraftPerformance.txt", $"performance {cycle}");
        Write(destDir, $"{fir}/Settings/VoiceChannels.txt", $"voice {cycle}");
        Write(destDir, $"{fir}/Settings/login_profiles.txt", $"logins {cycle}");

        if (includePersonalizationAndPlugins)
        {
            // Personalization (install baseline; omitted by updates).
            Write(destDir, $"{fir}/Settings/RADAR/symbology.txt", "symbology BASELINE");
            Write(destDir, $"{fir}/Settings/RADAR/tags.txt", "tags BASELINE");
            Write(destDir, $"{fir}/Settings/SOLO/general_settings.txt", "solo BASELINE");
            // Binary plugins (install only).
            Write(destDir, $"{fir}/Plugins/DiscordEuroscope.dll", "MZ synthetic dll");
        }
    }

    private static string Prf(string fir, string sctBase, string kind) =>
        string.Join('\n', new[]
        {
            $"Settings\tsector\t\\{sctBase}.sct",
            $"Settings\tSettingsfileSYMBOLOGY\t\\{fir}\\Settings\\{kind}\\symbology.txt",
            $"LastSession\tcallsign\t{fir}_CTR",
            $"LastSession\trange\t150",
        });

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
