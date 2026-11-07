using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using sinmai_launcher.Core.Models;

namespace sinmai_launcher.Core.Services
{
    public class GameScannerService : IGameScannerService
    {
        public Task<GameScanResult> ScanGameFolderAsync(string folderPath, GameType? expectedGame = null)
        {
            return Task.Run(() => ScanInternal(folderPath, expectedGame));
        }

        private GameScanResult ScanInternal(string folderPath, GameType? expectedGame)
        {
            var result = new GameScanResult
            {
                RootDirectory = folderPath
            };

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                result.IsValid = false;
                result.StatusMessage = "Selected directory does not exist.";
                return result;
            }

            // Check for Package subdirectory or direct root
            var packageDir = Path.Combine(folderPath, "Package");
            if (Directory.Exists(packageDir))
            {
                result.PackageDirectory = packageDir;
            }
            else
            {
                result.PackageDirectory = folderPath;
            }

            var searchDirs = new List<string>
            {
                folderPath,
                result.PackageDirectory,
                Path.Combine(folderPath, "bin"),
                Path.Combine(result.PackageDirectory, "Mods"),
                Path.Combine(result.PackageDirectory, "Plugins"),
                Path.Combine(result.PackageDirectory, "UserData")
            }.Distinct().Where(Directory.Exists).ToList();

            // Check for executables
            var sinmaiExe = searchDirs.Select(d => Path.Combine(d, "Sinmai.exe")).FirstOrDefault(File.Exists);
            var ongekiExe = searchDirs.Select(d => Path.Combine(d, "ongeki.exe")).FirstOrDefault(File.Exists);
            var chuniExe = searchDirs.Select(d => Path.Combine(d, "chuniApp.exe")).FirstOrDefault(File.Exists)
                ?? searchDirs.Select(d => Path.Combine(d, "chusanApp.exe")).FirstOrDefault(File.Exists);
            var daemonExe = searchDirs.Select(d => Path.Combine(d, "amdaemon.exe")).FirstOrDefault(File.Exists);

            // Determine game type and executable
            if (sinmaiExe != null)
            {
                result.DetectedGame = GameType.Maimai;
                result.AppExePath = sinmaiExe;
            }
            else if (ongekiExe != null)
            {
                result.DetectedGame = GameType.Ongeki;
                result.AppExePath = ongekiExe;
            }
            else if (chuniExe != null)
            {
                result.DetectedGame = GameType.Chunithm;
                result.AppExePath = chuniExe;
            }

            // Find launch script (.bat)
            // Priority: check parent directory of the detected game executable first (e.g. launch.bat / start.bat / game.bat next to Sinmai.exe)
            string? batFile = null;
            if (result.AppExePath != null)
            {
                var exeDir = Path.GetDirectoryName(result.AppExePath);
                if (!string.IsNullOrEmpty(exeDir) && Directory.Exists(exeDir))
                {
                    batFile = FindBatFileInDirectory(exeDir);
                }
            }

            // Fallback: check root game directory
            if (batFile == null)
            {
                batFile = FindBatFileInDirectory(folderPath);
            }

            if (result.DetectedGame == null && batFile != null && File.Exists(batFile))
            {
                try
                {
                    var batContent = File.ReadAllText(batFile);
                    if (batContent.Contains("Sinmai", StringComparison.OrdinalIgnoreCase) || batContent.Contains("SDGA", StringComparison.OrdinalIgnoreCase))
                    {
                        result.DetectedGame = GameType.Maimai;
                    }
                    else if (batContent.Contains("ongeki", StringComparison.OrdinalIgnoreCase) || batContent.Contains("SDDT", StringComparison.OrdinalIgnoreCase))
                    {
                        result.DetectedGame = GameType.Ongeki;
                    }
                    else if (batContent.Contains("chuni", StringComparison.OrdinalIgnoreCase) ||
                             batContent.Contains("chusan", StringComparison.OrdinalIgnoreCase) ||
                             batContent.Contains("SDBT", StringComparison.OrdinalIgnoreCase) ||
                             batContent.Contains("SDHD", StringComparison.OrdinalIgnoreCase))
                    {
                        result.DetectedGame = GameType.Chunithm;
                    }
                }
                catch { }
            }

            result.BatFilePath = batFile;
            result.DaemonExePath = daemonExe;

            if (result.DetectedGame == null && expectedGame != null)
            {
                result.DetectedGame = expectedGame;
            }

            var foundConfigs = new List<string>();

            if (result.DetectedGame == GameType.Maimai)
            {
                // Maimai DX specific configuration scanning per FEEDBACK.md:
                // Only scan AquaMai.toml, mai2.ini, segatools.ini, Sinmai-Assist\Config.yaml, Sinmai-Assist\KeyBindConfig.yaml, and DEVICE\aime.txt

                var segatoolsPath = searchDirs.Select(d => Path.Combine(d, "segatools.ini")).FirstOrDefault(File.Exists);
                var mai2Path = searchDirs.Select(d => Path.Combine(d, "mai2.ini")).FirstOrDefault(File.Exists);

                // Priority order: segatools.ini, mai2.ini, AquaMai.toml, Sinmai-Assist\Config.yaml, Sinmai-Assist\KeyBindConfig.yaml, aime.txt
                var aquaMaiPath = searchDirs.Select(d => Path.Combine(d, "AquaMai.toml")).FirstOrDefault(File.Exists);
                if (aquaMaiPath != null) foundConfigs.Add(aquaMaiPath);

                if (mai2Path != null) foundConfigs.Add(mai2Path);
                if (segatoolsPath != null) foundConfigs.Add(segatoolsPath);

                // Sinmai-Assist configs
                var sinmaiAssistDirs = new[]
                {
                    Path.Combine(result.PackageDirectory, "Sinmai-Assist"),
                    Path.Combine(folderPath, "Sinmai-Assist")
                };

                foreach (var sDir in sinmaiAssistDirs)
                {
                    if (Directory.Exists(sDir))
                    {
                        var cfgYaml = Path.Combine(sDir, "Config.yaml");
                        var cfgYml = Path.Combine(sDir, "Config.yml");
                        var targetCfg = File.Exists(cfgYaml) ? cfgYaml : (File.Exists(cfgYml) ? cfgYml : null);
                        if (targetCfg != null && !foundConfigs.Contains(targetCfg, StringComparer.OrdinalIgnoreCase))
                        {
                            foundConfigs.Add(targetCfg);
                        }

                        var kbYaml = Path.Combine(sDir, "KeyBindConfig.yaml");
                        var kbYml = Path.Combine(sDir, "KeyBindConfig.yml");
                        var targetKb = File.Exists(kbYaml) ? kbYaml : (File.Exists(kbYml) ? kbYml : null);
                        if (targetKb != null && !foundConfigs.Contains(targetKb, StringComparer.OrdinalIgnoreCase))
                        {
                            foundConfigs.Add(targetKb);
                        }
                        break;
                    }
                }

                // aime.txt in ./DEVICE (if not found, create a blank aime.txt file)
                var deviceDir = Path.Combine(result.PackageDirectory, "DEVICE");
                if (!Directory.Exists(deviceDir) && Directory.Exists(Path.Combine(folderPath, "DEVICE")))
                {
                    deviceDir = Path.Combine(folderPath, "DEVICE");
                }

                var aimeFile = Path.Combine(deviceDir, "aime.txt");
                if (!File.Exists(aimeFile))
                {
                    try
                    {
                        if (!Directory.Exists(deviceDir)) Directory.CreateDirectory(deviceDir);
                        File.WriteAllText(aimeFile, string.Empty);
                    }
                    catch { }
                }

                if (File.Exists(aimeFile) && !foundConfigs.Contains(aimeFile, StringComparer.OrdinalIgnoreCase))
                {
                    foundConfigs.Add(aimeFile);
                }

                result.DetectedConfigFiles = foundConfigs;
            }
            else if (result.DetectedGame == GameType.Chunithm)
            {
                // Chunithm specific configuration scanning per FEEDBACK.md:
                // Only render segatools.ini and aime.txt (strictly no duplicate segatools.ini or extraneous configs)
                var preferredSegatoolsLocations = new[]
                {
                    Path.Combine(folderPath, "segatools.ini"),
                    Path.Combine(result.PackageDirectory, "segatools.ini"),
                    Path.Combine(folderPath, "bin", "segatools.ini")
                };

                var segatoolsPath = preferredSegatoolsLocations.FirstOrDefault(File.Exists);
                if (segatoolsPath == null)
                {
                    segatoolsPath = searchDirs.Select(d => Path.Combine(d, "segatools.ini")).FirstOrDefault(File.Exists);
                }

                if (segatoolsPath == null)
                {
                    const string baseSegatools = @"C:\Users\Baconway\Downloads\segatools.ini";
                    if (File.Exists(baseSegatools))
                    {
                        try
                        {
                            var targetSegatools = Path.Combine(folderPath, "segatools.ini");
                            File.Copy(baseSegatools, targetSegatools, overwrite: false);
                            segatoolsPath = targetSegatools;
                        }
                        catch { }
                    }
                }

                if (segatoolsPath != null && !foundConfigs.Any(f => Path.GetFileName(f).Equals("segatools.ini", StringComparison.OrdinalIgnoreCase)))
                {
                    foundConfigs.Add(segatoolsPath);
                }

                // aime.txt in ./DEVICE (if not found, create a blank aime.txt file)
                var deviceDir = Path.Combine(folderPath, "DEVICE");
                if (!Directory.Exists(deviceDir) && Directory.Exists(Path.Combine(folderPath, "bin", "DEVICE")))
                {
                    deviceDir = Path.Combine(folderPath, "bin", "DEVICE");
                }

                var aimeFile = Path.Combine(deviceDir, "aime.txt");
                if (!File.Exists(aimeFile))
                {
                    try
                    {
                        if (!Directory.Exists(deviceDir)) Directory.CreateDirectory(deviceDir);
                        File.WriteAllText(aimeFile, string.Empty);
                    }
                    catch { }
                }

                if (File.Exists(aimeFile) && !foundConfigs.Any(f => Path.GetFileName(f).Equals("aime.txt", StringComparison.OrdinalIgnoreCase)))
                {
                    foundConfigs.Add(aimeFile);
                }

                result.DetectedConfigFiles = foundConfigs;
            }
            else
            {
                // General scanning for non-maimai games
                var configExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".ini", ".toml", ".yaml", ".yml", ".conf", ".cfg"
                };

                var knownConfigNames = new[]
                {
                    "segatools.ini",
                    "mai2.ini",
                    "maimai-rpc.ini",
                    "MuMod.toml",
                    "firewall.cfg",
                    "pxGetHwInfo.ini"
                };

                foreach (var name in knownConfigNames)
                {
                    foreach (var dir in searchDirs)
                    {
                        var p = Path.Combine(dir, name);
                        if (File.Exists(p) && !foundConfigs.Contains(p, StringComparer.OrdinalIgnoreCase))
                        {
                            foundConfigs.Add(p);
                        }
                    }
                }

                foreach (var dir in searchDirs)
                {
                    try
                    {
                        var files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
                        foreach (var f in files)
                        {
                            if (configExtensions.Contains(Path.GetExtension(f)) && !foundConfigs.Contains(f, StringComparer.OrdinalIgnoreCase))
                            {
                                foundConfigs.Add(f);
                            }
                        }
                    }
                    catch { }
                }

                result.DetectedConfigFiles = foundConfigs;
            }

            // Search for Mods in Package\Mods
            var modsDir = Path.Combine(result.PackageDirectory, "Mods");
            if (Directory.Exists(modsDir))
            {
                try
                {
                    var modFiles = Directory.GetFiles(modsDir, "*.dll", SearchOption.TopDirectoryOnly);
                    result.DetectedMods = modFiles.Select(Path.GetFileName).Where(s => s != null).Cast<string>().ToList();
                }
                catch { }
            }

            // Validation status
            if (result.DetectedGame == GameType.Maimai)
            {
                var hasSegatools = foundConfigs.Any(f => Path.GetFileName(f).Equals("segatools.ini", StringComparison.OrdinalIgnoreCase));
                var hasMai2 = foundConfigs.Any(f => Path.GetFileName(f).Equals("mai2.ini", StringComparison.OrdinalIgnoreCase));

                if (!hasSegatools || !hasMai2)
                {
                    result.IsValid = false;
                    result.StatusMessage = "Required configuration files missing: segatools.ini and mai2.ini must be present in the game directory.";
                }
                else if (result.BatFilePath != null || result.AppExePath != null)
                {
                    result.IsValid = true;
                    result.StatusMessage = $"Successfully detected maimai DX installation with {result.DetectedConfigFiles.Count} configuration files.";
                }
                else
                {
                    result.IsValid = false;
                    result.StatusMessage = "Could not find a valid game executable or launch batch script in the selected directory.";
                }
            }
            else if (result.DetectedGame == GameType.Chunithm)
            {
                var hasSegatools = foundConfigs.Any(f => Path.GetFileName(f).Equals("segatools.ini", StringComparison.OrdinalIgnoreCase));

                if (!hasSegatools)
                {
                    result.IsValid = false;
                    result.StatusMessage = "Required configuration file missing: segatools.ini must be present in the game directory.";
                }
                else if (result.BatFilePath != null || result.AppExePath != null)
                {
                    result.IsValid = true;
                    result.StatusMessage = $"Successfully detected CHUNITHM installation with {result.DetectedConfigFiles.Count} configuration files.";
                }
                else
                {
                    result.IsValid = false;
                    result.StatusMessage = "Could not find a valid game executable or launch batch script in the selected directory.";
                }
            }
            else if (result.DetectedGame != null && (result.BatFilePath != null || result.AppExePath != null))
            {
                result.IsValid = true;
                result.StatusMessage = $"Successfully detected {result.DetectedGame.Value.GetDisplayName()} installation with {result.DetectedConfigFiles.Count} configuration files.";
            }
            else
            {
                result.IsValid = false;
                result.StatusMessage = "Could not find a valid game executable or launch batch script in the selected directory.";
            }

            return result;
        }

        private static string? FindBatFileInDirectory(string dir)
        {
            var preferredNames = new[] { "launch.bat", "start.bat", "game.bat" };
            foreach (var name in preferredNames)
            {
                var p = Path.Combine(dir, name);
                if (File.Exists(p)) return p;
            }

            try
            {
                return Directory.GetFiles(dir, "*.bat", SearchOption.TopDirectoryOnly).FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }
    }
}
