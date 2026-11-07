using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using sinmai_launcher.Core.Models;
using YamlDotNet.RepresentationModel;

namespace sinmai_launcher.Core.Services.Parsers
{
    public class YamlConfigParser
    {
        public ConfigDocument Parse(string content, string filePath)
        {
            var document = new ConfigDocument
            {
                FilePath = filePath,
                Format = ConfigFormat.Yaml
            };

            var yaml = new YamlStream();
            using var reader = new StringReader(content);
            yaml.Load(reader);

            if (yaml.Documents.Count == 0)
            {
                return document;
            }

            var rootSection = new ConfigSection { Name = "Root" };

            if (yaml.Documents[0].RootNode is YamlMappingNode rootMapping)
            {
                foreach (var entry in rootMapping.Children)
                {
                    if (entry.Key is YamlScalarNode keyNode)
                    {
                        var key = keyNode.Value ?? string.Empty;

                        if (entry.Value is YamlScalarNode valueNode)
                        {
                            var val = valueNode.Value ?? string.Empty;
                            rootSection.Properties.Add(new ConfigProperty
                            {
                                Key = key,
                                RawValue = val,
                                ValueType = InferYamlValueType(key, val)
                            });
                        }
                        else if (entry.Value is YamlMappingNode subMapping)
                        {
                            var subSection = new ConfigSection { Name = key };
                            foreach (var subEntry in subMapping.Children)
                            {
                                if (subEntry.Key is YamlScalarNode subKeyNode && subEntry.Value is YamlScalarNode subValNode)
                                {
                                    var subKey = subKeyNode.Value ?? string.Empty;
                                    var subVal = subValNode.Value ?? string.Empty;
                                    subSection.Properties.Add(new ConfigProperty
                                    {
                                        Key = subKey,
                                        RawValue = subVal,
                                        ValueType = InferYamlValueType(subKey, subVal)
                                    });
                                }
                            }
                            document.Sections.Add(subSection);
                        }
                    }
                }
            }

            if (rootSection.Properties.Count > 0)
            {
                document.Sections.Insert(0, rootSection);
            }

            return document;
        }

        public string Serialize(ConfigDocument document)
        {
            var sb = new StringBuilder();

            foreach (var section in document.Sections)
            {
                if (section.Name != "Root")
                {
                    sb.AppendLine($"{section.Name}:");
                    foreach (var prop in section.Properties)
                    {
                        sb.AppendLine($"  {prop.Key}: {FormatYamlScalar(prop.RawValue, prop.ValueType)}");
                    }
                }
                else
                {
                    foreach (var prop in section.Properties)
                    {
                        sb.AppendLine($"{prop.Key}: {FormatYamlScalar(prop.RawValue, prop.ValueType)}");
                    }
                }
            }

            return sb.ToString();
        }

        private static string FormatYamlScalar(string value, PropertyValueType type)
        {
            if (type is PropertyValueType.Boolean or PropertyValueType.Integer or PropertyValueType.Float)
            {
                return value;
            }

            if (value.Contains(":") || value.Contains("#") || value.Contains(" ") || string.IsNullOrEmpty(value))
            {
                return $"\"{value.Replace("\"", "\\\"")}\"";
            }

            return value;
        }

        private static PropertyValueType InferYamlValueType(string key, string value)
        {
            var lower = value.ToLowerInvariant();
            if (lower is "true" or "false" or "yes" or "no")
            {
                return PropertyValueType.Boolean;
            }

            if (long.TryParse(value, out _))
            {
                return PropertyValueType.Integer;
            }

            if (double.TryParse(value, out _))
            {
                return PropertyValueType.Float;
            }

            if (key.ToLowerInvariant().Contains("path") || value.Contains("\\") || value.Contains("/"))
            {
                return PropertyValueType.FilePath;
            }

            return PropertyValueType.String;
        }
    }
}
