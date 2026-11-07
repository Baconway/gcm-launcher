using System.Collections.Generic;
using sinmai_launcher.Core.Models;

namespace sinmai_launcher.Core.Services
{
    public interface IValidationService
    {
        bool ValidateProperty(ConfigProperty property, string basePath, out string? errorMessage);
        bool ValidateFilePath(string path, string basePath, IEnumerable<string>? allowedExtensions, out string? errorMessage);
        bool ValidateDirectoryPath(string path, string basePath, out string? errorMessage);
        bool ValidateNumeric(string value, bool isInteger, out string? errorMessage);
        bool ValidateBoolean(string value, out string? errorMessage);
    }
}
