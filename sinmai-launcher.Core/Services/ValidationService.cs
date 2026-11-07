using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using sinmai_launcher.Core.Models;

namespace sinmai_launcher.Core.Services
{
    public class ValidationService : IValidationService
    {
        private static readonly HashSet<string> ValidBools = new(StringComparer.OrdinalIgnoreCase)
        {
            "1", "0", "true", "false", "yes", "no", "on", "off"
        };

        public bool ValidateBoolean(string value, out string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errorMessage = "Boolean value cannot be empty.";
                return false;
            }

            if (ValidBools.Contains(value.Trim()))
            {
                errorMessage = null;
                return true;
            }

            errorMessage = $"'{value}' is not a valid boolean. Accepted values: 1, 0, true, false, yes, no.";
            return false;
        }

        public bool ValidateNumeric(string value, bool isInteger, out string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errorMessage = "Numeric value cannot be empty.";
                return false;
            }

            var trimmed = value.Trim();
            if (isInteger)
            {
                if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    errorMessage = null;
                    return true;
                }
                errorMessage = $"'{value}' is not a valid integer.";
                return false;
            }
            else
            {
                if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    errorMessage = null;
                    return true;
                }
                errorMessage = $"'{value}' is not a valid floating point number.";
                return false;
            }
        }

        public bool ValidateFilePath(string path, string basePath, IEnumerable<string>? allowedExtensions, out string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                errorMessage = "File path cannot be empty.";
                return false;
            }

            var cleanPath = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"', '\''));

            // Check extension if restricted
            if (allowedExtensions != null && allowedExtensions.Any())
            {
                var ext = Path.GetExtension(cleanPath);
                if (string.IsNullOrEmpty(ext) || !allowedExtensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)))
                {
                    errorMessage = $"Invalid file extension '{ext}'. Allowed extensions: {string.Join(", ", allowedExtensions)}.";
                    return false;
                }
            }

            // Check existence
            var resolvedPath = Path.IsPathRooted(cleanPath)
                ? cleanPath
                : Path.GetFullPath(Path.Combine(basePath, cleanPath));

            if (!File.Exists(resolvedPath))
            {
                errorMessage = $"File does not exist: '{cleanPath}'.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public bool ValidateDirectoryPath(string path, string basePath, out string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                errorMessage = "Directory path cannot be empty.";
                return false;
            }

            var cleanPath = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"', '\''));
            var resolvedPath = Path.IsPathRooted(cleanPath)
                ? cleanPath
                : Path.GetFullPath(Path.Combine(basePath, cleanPath));

            if (!Directory.Exists(resolvedPath))
            {
                errorMessage = $"Directory does not exist: '{cleanPath}'.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        public bool ValidateProperty(ConfigProperty property, string basePath, out string? errorMessage)
        {
            errorMessage = null;

            if (property.IsOptional && (string.IsNullOrWhiteSpace(property.RawValue) || property.RawValue.Equals("(Disabled)", StringComparison.OrdinalIgnoreCase)))
            {
                property.IsValid = true;
                property.ValidationErrorMessage = null;
                return true;
            }

            bool valid;

            switch (property.ValueType)
            {
                case PropertyValueType.Boolean:
                    valid = ValidateBoolean(property.RawValue, out errorMessage);
                    break;
                case PropertyValueType.Integer:
                    valid = ValidateNumeric(property.RawValue, isInteger: true, out errorMessage);
                    break;
                case PropertyValueType.Float:
                    valid = ValidateNumeric(property.RawValue, isInteger: false, out errorMessage);
                    break;
                case PropertyValueType.FilePath:
                    valid = ValidateFilePath(property.RawValue, basePath, property.AllowedExtensions, out errorMessage);
                    break;
                case PropertyValueType.DirectoryPath:
                    valid = ValidateDirectoryPath(property.RawValue, basePath, out errorMessage);
                    break;
                case PropertyValueType.String:
                default:
                    valid = true;
                    break;
            }

            property.IsValid = valid;
            property.ValidationErrorMessage = errorMessage;
            return valid;
        }
    }
}
