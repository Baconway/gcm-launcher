using System.Collections.Generic;

namespace sinmai_launcher.Core.Models
{
    public class GameScanResult
    {
        public GameType? DetectedGame { get; set; }
        public bool IsValid { get; set; }
        public string RootDirectory { get; set; } = string.Empty;
        public string? PackageDirectory { get; set; }
        public string? BatFilePath { get; set; }
        public string? AppExePath { get; set; }
        public string? DaemonExePath { get; set; }
        public List<string> DetectedConfigFiles { get; set; } = new();
        public List<string> DetectedMods { get; set; } = new();
        public string StatusMessage { get; set; } = string.Empty;
    }
}
