using System.Collections.Generic;
using System.Threading.Tasks;
using sinmai_launcher.Core.Models;

namespace sinmai_launcher.Core.Services
{
    public interface IConfigService
    {
        Task<ConfigDocument> LoadConfigAsync(string filePath, string gameDirectory);
        Task SaveConfigAsync(ConfigDocument document);
        ConfigFormat DetectFormat(string filePath);
        Task<ConfigValidationSummary> ValidateGameConfigurationsAsync(string gameDirectory, IEnumerable<string> configPaths);
    }
}
