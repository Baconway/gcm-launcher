using System.Collections.Generic;

namespace sinmai_launcher.Core.Models
{
    public class ConfigValidationSummary
    {
        public bool IsValid { get; set; } = true;
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> ValidFiles { get; set; } = new();
    }
}
