using System.Text.Json;

namespace Sts2Mod.StateBridge.Configuration;

public sealed record InstallationProbeResult(
    bool RuntimeAvailable,
    string? ManagedDir,
    string? ModLoaderDir,
    string? GameVersion,
    IReadOnlyList<string> Notes);

public static class Sts2InstallationLocator
{
    public static InstallationProbeResult Discover(BridgeOptions options)
    {
        var notes = new List<string>();
        var managedCandidates = GetManagedCandidates(options).ToArray();
        var modLoaderCandidates = GetModLoaderCandidates(options).ToArray();

        var managedDir = managedCandidates.FirstOrDefault(ContainsManagedAssemblies);
        var modLoaderDir = modLoaderCandidates.FirstOrDefault(ContainsModLoaderAssemblies);

        if (modLoaderDir is null && managedDir is not null && ContainsModLoaderAssemblies(managedDir))
        {
            modLoaderDir = managedDir;
            notes.Add("mod loader assemblies were discovered in the managed directory");
        }

        if (managedDir is null)
        {
            notes.Add("sts2 managed directory was not found");
        }

        if (modLoaderDir is null)
        {
            notes.Add("mod loader directory was not found");
        }

        var runtimeAvailable = managedDir is not null;
        var gameVersion = managedDir is null ? options.GameVersion : ReadGameVersion(managedDir) ?? options.GameVersion;
        return new InstallationProbeResult(runtimeAvailable, managedDir, modLoaderDir, gameVersion, notes);
    }

    private static IEnumerable<string> GetManagedCandidates(BridgeOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Sts2ManagedDir))
        {
            yield return options.Sts2ManagedDir;
        }

        var envValue = Environment.GetEnvironmentVariable("STS2_MANAGED_DIR");
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            yield return envValue;
        }

        foreach (var root in new[]
                 {
                     @"C:\Program Files (x86)\Steam\steamapps\common",
                     @"C:\Program Files\Steam\steamapps\common",
                     @"D:\SteamLibrary\steamapps\common",
                     @"E:\SteamLibrary\steamapps\common",
                     @"F:\SteamLibrary\steamapps\common",
                 })
        {
            foreach (var suffix in new[]
                     {
                         @"Slay the Spire 2\data_sts2_windows_x86_64",
                         @"Slay the Spire 2\Game",
                         @"Slay the Spire 2",
                         @"SlayTheSpire2\data_sts2_windows_x86_64",
                         @"SlayTheSpire2\Game",
                         @"SlayTheSpire2",
                     })
            {
                yield return Path.Combine(root, suffix);
            }
        }
    }

    private static IEnumerable<string> GetModLoaderCandidates(BridgeOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Sts2ModLoaderDir))
        {
            yield return options.Sts2ModLoaderDir;
        }

        var envValue = Environment.GetEnvironmentVariable("STS2_MODLOADER_DIR");
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            yield return envValue;
        }

        foreach (var managedCandidate in GetManagedCandidates(options))
        {
            yield return managedCandidate;
        }

        foreach (var root in new[]
                 {
                     @"C:\Program Files (x86)\Steam\steamapps\workshop\content",
                     @"C:\Program Files\Steam\steamapps\workshop\content",
                     @"D:\SteamLibrary\steamapps\workshop\content",
                     @"E:\SteamLibrary\steamapps\workshop\content",
                     @"F:\SteamLibrary\steamapps\workshop\content",
                     @"E:\mods\sts2",
                     @"F:\mods\sts2",
                 })
        {
            yield return root;
        }
    }

    private static bool ContainsManagedAssemblies(string candidate)
    {
        return Directory.Exists(candidate) &&
               File.Exists(Path.Combine(candidate, "sts2.dll")) &&
               File.Exists(Path.Combine(candidate, "GodotSharp.dll"));
    }

    private static bool ContainsModLoaderAssemblies(string candidate)
    {
        return Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "0Harmony.dll"));
    }

    private static string? ReadGameVersion(string managedDir)
    {
        var releaseInfoPath = Path.Combine(Directory.GetParent(managedDir)?.FullName ?? managedDir, "release_info.json");
        if (File.Exists(releaseInfoPath))
        {
            try
            {
                using var stream = File.OpenRead(releaseInfoPath);
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                if (root.TryGetProperty("version", out var versionElement))
                {
                    var version = versionElement.GetString();
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        if (root.TryGetProperty("commit", out var commitElement))
                        {
                            var commit = commitElement.GetString();
                            if (!string.IsNullOrWhiteSpace(commit))
                            {
                                return $"{version} ({commit})";
                            }
                        }

                        return version;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        var dllPath = Path.Combine(managedDir, "sts2.dll");
        if (!File.Exists(dllPath))
        {
            return null;
        }

        return System.Diagnostics.FileVersionInfo.GetVersionInfo(dllPath).FileVersion;
    }
}
