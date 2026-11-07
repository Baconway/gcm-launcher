using System.Collections.Generic;

namespace sinmai_launcher.Core.Models
{
    public enum PropertyValueType
    {
        String,
        Boolean,
        Integer,
        Float,
        FilePath,
        DirectoryPath
    }

    public class ConfigProperty
    {
        public string Key { get; set; } = string.Empty;
        public string RawValue { get; set; } = string.Empty;
        public PropertyValueType ValueType { get; set; } = PropertyValueType.String;
        public string? Comment { get; set; }
        public List<string> AllowedExtensions { get; set; } = new();
        public List<string>? Options { get; set; }
        public bool IsValid { get; set; } = true;
        public string? ValidationErrorMessage { get; set; }
        public bool IsCommentedOut { get; set; }
        public bool IsOptional { get; set; }
        public bool AllowCustomOptions { get; set; }

        public bool AsBoolean()
        {
            var val = RawValue.Trim().ToLowerInvariant();
            return val is "1" or "true" or "yes" or "on";
        }

        public void SetFromBoolean(bool value, bool asNumeric = false)
        {
            if (asNumeric)
            {
                RawValue = value ? "1" : "0";
            }
            else
            {
                RawValue = value ? "true" : "false";
            }
        }
    }
}
