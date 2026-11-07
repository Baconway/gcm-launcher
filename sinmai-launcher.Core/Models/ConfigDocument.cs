using System.Collections.Generic;

namespace sinmai_launcher.Core.Models
{
    public class ConfigSection
    {
        public string Name { get; set; } = string.Empty;
        public string? Comment { get; set; }
        public List<ConfigProperty> Properties { get; set; } = new();
    }

    public enum ConfigFormat
    {
        Ini,
        Toml,
        Yaml,
        Conf
    }

    public class ConfigDocument
    {
        public string FilePath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public ConfigFormat Format { get; set; } = ConfigFormat.Ini;
        public string? HeaderComment { get; set; }
        public List<ConfigSection> Sections { get; set; } = new();
    }
}
