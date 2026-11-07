using System;
using System.IO;
using System.Threading.Tasks;
using sinmai_launcher.Core.Models;
using sinmai_launcher.Core.Services.Parsers;

namespace sinmai_launcher.Core.Services
{
    public class ConfigService : IConfigService
    {
        private readonly IValidationService _validationService;
        private readonly IniConfigParser _iniParser = new();
        private readonly TomlConfigParser _tomlParser = new();
        private readonly YamlConfigParser _yamlParser = new();

        public ConfigService(IValidationService validationService)
        {
            _validationService = validationService;
        }

        public ConfigFormat DetectFormat(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".toml" => ConfigFormat.Toml,
                ".yaml" or ".yml" => ConfigFormat.Yaml,
                ".conf" or ".cfg" => ConfigFormat.Conf,
                _ => ConfigFormat.Ini
            };
        }

        public async Task<ConfigDocument> LoadConfigAsync(string filePath, string gameDirectory)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Configuration file not found: {filePath}");
            }

            // Handle aime.txt
            if (Path.GetFileName(filePath).Equals("aime.txt", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(filePath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                var accessCode = (await File.ReadAllTextAsync(filePath)).Trim();
                var aimeDoc = new ConfigDocument
                {
                    FilePath = filePath,
                    Format = ConfigFormat.Ini
                };
                var aimeSection = new ConfigSection
                {
                    Name = "Aime Card",
                    Comment = "Aime card access code configuration (stored in DEVICE\\aime.txt)"
                };
                aimeSection.Properties.Add(new ConfigProperty
                {
                    Key = "AccessCode",
                    RawValue = accessCode,
                    Comment = "20-digit Sega Aime Access Code (e.g. 50142102613307751353). Used for player login and card reader emulation.",
                    ValueType = PropertyValueType.String
                });
                aimeDoc.Sections.Add(aimeSection);
                try { aimeDoc.RelativePath = Path.GetRelativePath(gameDirectory, filePath); }
                catch { aimeDoc.RelativePath = Path.GetFileName(filePath); }
                return aimeDoc;
            }

            var format = DetectFormat(filePath);
            var content = await File.ReadAllTextAsync(filePath);

            ConfigDocument doc = format switch
            {
                ConfigFormat.Toml => _tomlParser.Parse(content, filePath),
                ConfigFormat.Yaml => _yamlParser.Parse(content, filePath),
                ConfigFormat.Conf => _iniParser.Parse(content, filePath, ConfigFormat.Conf),
                _ => _iniParser.Parse(content, filePath, ConfigFormat.Ini)
            };

            // Calculate relative path
            try
            {
                doc.RelativePath = Path.GetRelativePath(gameDirectory, filePath);
            }
            catch
            {
                doc.RelativePath = Path.GetFileName(filePath);
            }

            // Validate all properties against gameDirectory
            foreach (var section in doc.Sections)
            {
                foreach (var prop in section.Properties)
                {
                    _validationService.ValidateProperty(prop, gameDirectory, out _);
                }
            }

            return doc;
        }

        public async Task SaveConfigAsync(ConfigDocument document)
        {
            var filePath = document.FilePath;
            if (string.IsNullOrEmpty(filePath))
            {
                throw new InvalidOperationException("ConfigDocument has no FilePath set.");
            }

            // Create backup if file exists
            if (File.Exists(filePath))
            {
                var backupPath = filePath + ".bak";
                if (!File.Exists(backupPath))
                {
                    try { File.Copy(filePath, backupPath, false); } catch { }
                }
            }

            // Handle aime.txt
            if (Path.GetFileName(filePath).Equals("aime.txt", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(filePath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                var accessProp = document.Sections.SelectMany(s => s.Properties).FirstOrDefault();
                var text = accessProp?.RawValue?.Trim() ?? string.Empty;
                await File.WriteAllTextAsync(filePath, text);
                return;
            }

            string content = document.Format switch
            {
                ConfigFormat.Toml => _tomlParser.Serialize(document),
                ConfigFormat.Yaml => _yamlParser.Serialize(document),
                _ => _iniParser.Serialize(document)
            };

            await File.WriteAllTextAsync(filePath, content);
        }

        public async Task<ConfigValidationSummary> ValidateGameConfigurationsAsync(string gameDirectory, IEnumerable<string> configPaths)
        {
            var summary = new ConfigValidationSummary();

            foreach (var path in configPaths)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;

                try
                {
                    var doc = await LoadConfigAsync(path, gameDirectory);
                    summary.ValidFiles.Add(Path.GetFileName(path));

                    foreach (var section in doc.Sections)
                    {
                        foreach (var prop in section.Properties)
                        {
                            if (prop.IsCommentedOut) continue;

                            if (!_validationService.ValidateProperty(prop, gameDirectory, out var err))
                            {
                                if (prop.ValueType is PropertyValueType.Boolean or PropertyValueType.Integer or PropertyValueType.Float)
                                {
                                    summary.IsValid = false;
                                    summary.Errors.Add($"{Path.GetFileName(path)} [{section.Name}] {prop.Key}: {err}");
                                }
                                else if (prop.ValueType is PropertyValueType.FilePath && !string.IsNullOrWhiteSpace(prop.RawValue))
                                {
                                    summary.Warnings.Add($"{Path.GetFileName(path)} [{section.Name}] {prop.Key}: {err}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    summary.IsValid = false;
                    summary.Errors.Add($"Error in {Path.GetFileName(path)}: {ex.Message}");
                }
            }

            return summary;
        }
    }
}
