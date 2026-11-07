using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using sinmai_launcher.Core.Models;

namespace sinmai_launcher.Core.Services
{
    public class LaunchService : ILaunchService
    {
        private Process? _runningProcess;
        public bool IsGameRunning => _runningProcess != null && !_runningProcess.HasExited;

        public event EventHandler<bool>? GameStateChanged;

        public string GenerateLaunchScript(string originalBatContent, LaunchSettings settings)
        {
            var lines = originalBatContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();

            foreach (var rawLine in lines)
            {
                var line = rawLine;

                // 1. Console mode handling for Daemon (amdaemon)
                if ((line.Contains("%DAEMON_EXE%", StringComparison.OrdinalIgnoreCase) || line.Contains("amdaemon", StringComparison.OrdinalIgnoreCase)) && line.TrimStart().StartsWith("start", StringComparison.OrdinalIgnoreCase))
                {
                    switch (settings.ConsoleMode)
                    {
                        case ConsoleOutputMode.Show:
                            line = Regex.Replace(line, @"/min\s*", "", RegexOptions.IgnoreCase);
                            line = Regex.Replace(line, @"/b\s*", "", RegexOptions.IgnoreCase);
                            break;
                        case ConsoleOutputMode.Hidden:
                            if (Regex.IsMatch(line, @"/min", RegexOptions.IgnoreCase))
                            {
                                line = Regex.Replace(line, @"/min\s*", "/B ", RegexOptions.IgnoreCase);
                            }
                            else if (!Regex.IsMatch(line, @"/b\b", RegexOptions.IgnoreCase))
                            {
                                line = Regex.Replace(line, @"start\s+", "start /B ", RegexOptions.IgnoreCase);
                            }
                            break;
                        case ConsoleOutputMode.Minimized:
                        default:
                            if (!Regex.IsMatch(line, @"/min\b", RegexOptions.IgnoreCase))
                            {
                                if (Regex.IsMatch(line, @"/b\b", RegexOptions.IgnoreCase))
                                {
                                    line = Regex.Replace(line, @"/b\s*", "/min ", RegexOptions.IgnoreCase);
                                }
                                else
                                {
                                    line = Regex.Replace(line, @"start\s+", "start /min ", RegexOptions.IgnoreCase);
                                }
                            }
                            break;
                    }
                }

                // 2. Screen parameters on application launch line
                if (line.Contains("%APP_EXE%", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Sinmai", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("ongeki", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("chuni", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("chusan", StringComparison.OrdinalIgnoreCase))
                {
                    // Fullscreen
                    int fsVal = settings.Fullscreen ? 1 : 0;
                    if (Regex.IsMatch(line, @"-screen-fullscreen\s+\d+"))
                    {
                        line = Regex.Replace(line, @"-screen-fullscreen\s+\d+", $"-screen-fullscreen {fsVal}");
                    }
                    else
                    {
                        line += $" -screen-fullscreen {fsVal}";
                    }

                    // Width
                    if (Regex.IsMatch(line, @"-screen-width\s+\d+"))
                    {
                        line = Regex.Replace(line, @"-screen-width\s+\d+", $"-screen-width {settings.ScreenWidth}");
                    }
                    else
                    {
                        line += $" -screen-width {settings.ScreenWidth}";
                    }

                    // Height
                    if (Regex.IsMatch(line, @"-screen-height\s+\d+"))
                    {
                        line = Regex.Replace(line, @"-screen-height\s+\d+", $"-screen-height {settings.ScreenHeight}");
                    }
                    else
                    {
                        line += $" -screen-height {settings.ScreenHeight}";
                    }

                    // Popup window
                    if (settings.PopupWindow)
                    {
                        if (!line.Contains("-popupwindow", StringComparison.OrdinalIgnoreCase))
                        {
                            line += " -popupwindow";
                        }
                    }
                    else
                    {
                        line = Regex.Replace(line, @"\s*-popupwindow", "");
                    }
                }

                sb.AppendLine(line);
            }

            return sb.ToString();
        }

        public void UpdateLaunchScriptInPlace(string batPath, LaunchSettings settings)
        {
            if (string.IsNullOrEmpty(batPath) || !File.Exists(batPath)) return;

            var origBackup = batPath + ".orig";
            if (!File.Exists(origBackup))
            {
                try
                {
                    File.Copy(batPath, origBackup, overwrite: false);
                }
                catch { }
            }

            var content = File.ReadAllText(batPath);
            var modified = GenerateLaunchScript(content, settings);
            File.WriteAllText(batPath, modified);
        }

        public async Task<LaunchProcessResult> LaunchGameAsync(Profile profile)
        {
            var result = new LaunchProcessResult();

            if (IsGameRunning)
            {
                result.Success = false;
                result.ErrorMessage = "A game session is already running.";
                return result;
            }

            var gameDir = profile.GameDirectory;
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
            {
                result.Success = false;
                result.ErrorMessage = $"Game directory not found: {gameDir}";
                return result;
            }

            var batPath = profile.LaunchSettings.BatFilePath;
            if (string.IsNullOrEmpty(batPath) || !File.Exists(batPath))
            {
                var pkgLaunch = Path.Combine(gameDir, "Package", "launch.bat");
                if (File.Exists(pkgLaunch))
                {
                    batPath = pkgLaunch;
                }
                else
                {
                    batPath = Path.Combine(gameDir, "game.bat");
                }
            }

            if (!File.Exists(batPath))
            {
                result.Success = false;
                result.ErrorMessage = $"Launch script not found: {batPath}";
                return result;
            }

            try
            {
                // Update the current launch script in-place with original backup
                UpdateLaunchScriptInPlace(batPath, profile.LaunchSettings);
                result.GeneratedBatchContent = await File.ReadAllTextAsync(batPath);

                // Clean up any legacy sinmai_launch.bat if it existed previously
                var legacyLaunch = Path.Combine(gameDir, "sinmai_launch.bat");
                if (File.Exists(legacyLaunch))
                {
                    try { File.Delete(legacyLaunch); } catch { }
                }

                var batDir = Path.GetDirectoryName(batPath) ?? gameDir;
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{batPath}\"\"",
                    WorkingDirectory = batDir,
                    UseShellExecute = true
                };

                _runningProcess = Process.Start(startInfo);
                if (_runningProcess != null)
                {
                    result.Success = true;
                    result.ProcessId = _runningProcess.Id;
                    profile.LastPlayedDate = DateTimeOffset.Now;

                    _runningProcess.EnableRaisingEvents = true;
                    _runningProcess.Exited += (s, e) =>
                    {
                        _runningProcess = null;
                        GameStateChanged?.Invoke(this, false);
                    };

                    GameStateChanged?.Invoke(this, true);
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "Failed to start process.";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        public void StopGame()
        {
            if (_runningProcess != null && !_runningProcess.HasExited)
            {
                try
                {
                    var killInfo = new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/F /T /PID {_runningProcess.Id}",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(killInfo)?.WaitForExit(3000);
                }
                catch { }

                try
                {
                    if (!_runningProcess.HasExited)
                    {
                        _runningProcess.Kill(entireProcessTree: true);
                    }
                }
                catch { }

                _runningProcess = null;
                GameStateChanged?.Invoke(this, false);
            }
        }
    }
}
