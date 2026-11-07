using System;
using System.Diagnostics;
using System.Threading.Tasks;
using sinmai_launcher.Core.Models;

namespace sinmai_launcher.Core.Services
{
    public class LaunchProcessResult
    {
        public bool Success { get; set; }
        public int? ProcessId { get; set; }
        public string? ErrorMessage { get; set; }
        public string? GeneratedBatchContent { get; set; }
    }

    public interface ILaunchService
    {
        string GenerateLaunchScript(string originalBatContent, LaunchSettings settings);
        void UpdateLaunchScriptInPlace(string batPath, LaunchSettings settings);
        Task<LaunchProcessResult> LaunchGameAsync(Profile profile);
        bool IsGameRunning { get; }
        event EventHandler<bool>? GameStateChanged;
        void StopGame();
    }
}
