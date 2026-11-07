using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using sinmai_launcher.Core.Models;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Syntax;

namespace sinmai_launcher.Core.Services.Parsers
{
    public class TomlConfigParser
    {
        public ConfigDocument Parse(string content, string filePath)
        {
            var document = new ConfigDocument
            {
                FilePath = filePath,
                Format = ConfigFormat.Toml
            };

            // Parse lines to preserve section hierarchy and comments
            var currentSection = new ConfigSection { Name = "Root" };
            var currentComments = new StringBuilder();
            var headerComments = new StringBuilder();
            bool hasEncounteredSection = false;

            using var reader = new StringReader(content);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    if (!hasEncounteredSection && currentSection.Properties.Count == 0 && currentComments.Length > 0)
                    {
                        if (headerComments.Length > 0) headerComments.AppendLine();
                        headerComments.Append(currentComments.ToString().TrimEnd());
                        currentComments.Clear();
                    }
                    continue;
                }

                // Comment line
                if (trimmed.StartsWith("#"))
                {
                    // Check if it's a commented-out key-value: e.g. "#Paths = ..." or "#AddAsFallback = true"
                    var commentContent = trimmed.TrimStart('#').Trim();
                    var commentEquals = commentContent.IndexOf('=');
                    if (commentEquals > 0 && !trimmed.StartsWith("##") && !commentContent.StartsWith("["))
                    {
                        var cKey = commentContent.Substring(0, commentEquals).Trim();
                        var cVal = commentContent.Substring(commentEquals + 1).Trim().Trim('"', '\'');
                        if (IsValidIdentifier(cKey))
                        {
                            var cProp = new ConfigProperty
                            {
                                Key = cKey,
                                RawValue = cVal,
                                Comment = currentComments.Length > 0 ? currentComments.ToString().TrimEnd() : null,
                                IsCommentedOut = true,
                                ValueType = InferTomlValueType(cKey, cVal)
                            };
                            ApplySchemaRules(cProp, currentSection.Name);
                            currentSection.Properties.Add(cProp);
                            currentComments.Clear();
                            continue;
                        }
                    }

                    currentComments.AppendLine(trimmed);
                    continue;
                }

                // Section / Table header [Table] or [[Array]]
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    hasEncounteredSection = true;
                    if (currentSection.Properties.Count > 0)
                    {
                        AddOrMergeSection(document, currentSection);
                    }

                    var sectionName = trimmed.TrimStart('[').TrimEnd(']').Trim();
                    var existing = document.Sections.Find(s => s.Name == sectionName);
                    if (existing != null)
                    {
                        currentSection = existing;
                    }
                    else
                    {
                        currentSection = new ConfigSection
                        {
                            Name = sectionName,
                            Comment = currentComments.Length > 0 ? currentComments.ToString().TrimEnd() : null
                        };
                    }
                    currentComments.Clear();
                    continue;
                }

                // Key = Value
                var equalsIdx = trimmed.IndexOf('=');
                if (equalsIdx > 0)
                {
                    var key = trimmed.Substring(0, equalsIdx).Trim();
                    var valPart = trimmed.Substring(equalsIdx + 1).Trim();

                    // Strip inline comment
                    string? inlineComment = null;
                    var hashIdx = valPart.IndexOf('#');
                    if (hashIdx >= 0)
                    {
                        inlineComment = valPart.Substring(hashIdx).Trim();
                        valPart = valPart.Substring(0, hashIdx).Trim();
                    }

                    var cleanValue = valPart.Trim('"', '\'');
                    var commentText = currentComments.Length > 0 ? currentComments.ToString().TrimEnd() : null;
                    if (!string.IsNullOrEmpty(inlineComment))
                    {
                        commentText = string.IsNullOrEmpty(commentText) ? inlineComment : commentText + " " + inlineComment;
                    }

                    var prop = new ConfigProperty
                    {
                        Key = key,
                        RawValue = cleanValue,
                        Comment = commentText,
                        IsCommentedOut = false,
                        ValueType = InferTomlValueType(key, cleanValue)
                    };

                    ApplySchemaRules(prop, currentSection.Name);
                    currentSection.Properties.Add(prop);
                    currentComments.Clear();
                }
            }

            if (currentSection.Properties.Count > 0)
            {
                AddOrMergeSection(document, currentSection);
            }

            document.HeaderComment = headerComments.Length > 0 ? headerComments.ToString().TrimEnd() : null;
            return document;
        }

        private static void AddOrMergeSection(ConfigDocument document, ConfigSection section)
        {
            var existing = document.Sections.Find(s => s.Name == section.Name);
            if (existing != null)
            {
                foreach (var prop in section.Properties)
                {
                    if (!existing.Properties.Exists(p => p.Key == prop.Key))
                    {
                        existing.Properties.Add(prop);
                    }
                }
            }
            else
            {
                document.Sections.Add(section);
            }
        }

        public string Serialize(ConfigDocument document)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(document.HeaderComment))
            {
                sb.AppendLine(document.HeaderComment);
                sb.AppendLine();
            }

            foreach (var section in document.Sections)
            {
                if (section.Name != "Root" && (section.Name != "General" || document.Sections.Count > 1))
                {
                    if (!string.IsNullOrEmpty(section.Comment))
                    {
                        sb.AppendLine(section.Comment);
                    }
                    sb.AppendLine($"[{section.Name}]");
                }

                foreach (var prop in section.Properties)
                {
                    if (!string.IsNullOrEmpty(prop.Comment))
                    {
                        sb.AppendLine(prop.Comment);
                    }

                    var prefix = prop.IsCommentedOut ? "#" : "";
                    var formattedValue = FormatTomlValue(prop.RawValue, prop.ValueType);
                    sb.AppendLine($"{prefix}{prop.Key} = {formattedValue}");
                }

                sb.AppendLine();
            }

            return sb.ToString().TrimEnd() + Environment.NewLine;
        }

        private static bool IsValidIdentifier(string s)
        {
            return Regex.IsMatch(s, @"^[a-zA-Z0-9_\-]+$");
        }

        private static PropertyValueType InferTomlValueType(string key, string value)
        {
            var lowerVal = value.ToLowerInvariant();
            if (lowerVal is "true" or "false")
            {
                return PropertyValueType.Boolean;
            }

            var lowerKey = key.ToLowerInvariant();
            if (lowerKey.Contains("path") || lowerKey.Contains("file") || lowerKey.Contains("dir") || value.Contains("\\") || value.Contains("/"))
            {
                return PropertyValueType.FilePath;
            }

            if (long.TryParse(value, out _))
            {
                return PropertyValueType.Integer;
            }

            if (double.TryParse(value, out _))
            {
                return PropertyValueType.Float;
            }

            return PropertyValueType.String;
        }

        public static void ApplySchemaRules(ConfigProperty prop, string sectionName)
        {
            var lowerKey = prop.Key.ToLowerInvariant();
            var lowerSection = sectionName.ToLowerInvariant();

            // Extract options from comments
            var extracted = ExtractOptionsFromComment(prop.Comment);
            if (extracted != null && extracted.Count >= 2)
            {
                prop.Options = extracted;
            }

            // Schema-defined option dropdowns
            if (lowerKey == "locale")
            {
                prop.Options ??= new List<string> { "en", "zh" };
            }
            else if (lowerKey == "channel")
            {
                prop.Options ??= new List<string> { "fast", "stable", "beta" };
            }
            else if (lowerKey == "sound8ch")
            {
                prop.Options ??= new List<string> { "0", "1" };
            }
            else if (lowerKey == "target" && lowerSection == "am")
            {
                prop.Options ??= new List<string> { "0", "1" };
            }
            else if (lowerKey.StartsWith("p1button") || lowerKey.StartsWith("p2button"))
            {
                prop.Options ??= new List<string> { "Select1P", "Select2P", "Service", "Test" };
            }

            // Boolean keys schema enforcement
            if (lowerKey == "disabled" || lowerKey == "enable" || lowerKey == "enabled" ||
                lowerKey.StartsWith("enable") || lowerKey.StartsWith("disable") || lowerKey.EndsWith("enabled"))
            {
                prop.ValueType = PropertyValueType.Boolean;
            }

            // Font path rules
            if (lowerSection.Contains("font") || lowerKey.Contains("font") || lowerKey.EndsWith("paths"))
            {
                prop.ValueType = PropertyValueType.FilePath;
                prop.AllowedExtensions = new List<string> { ".ttc", ".ttf", ".otf" };
            }
            // Movie/Video path rules
            else if (lowerSection.Contains("movie") || lowerKey.Contains("movie") || lowerKey.Contains("video"))
            {
                if (lowerKey.Contains("path") || lowerKey.Contains("dir"))
                {
                    prop.ValueType = PropertyValueType.DirectoryPath;
                }
                else if (prop.RawValue.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    prop.ValueType = PropertyValueType.FilePath;
                    prop.AllowedExtensions = new List<string> { ".mp4", ".mov", ".mkv", ".avi" };
                }
            }
            // Image / Asset dir rules
            else if (lowerKey.Contains("imageassetsdir") || lowerKey.EndsWith("dir"))
            {
                prop.ValueType = PropertyValueType.DirectoryPath;
            }
            // Cache paths
            else if (lowerKey.Contains("cache"))
            {
                prop.ValueType = PropertyValueType.FilePath;
                prop.AllowedExtensions = new List<string> { ".cache", ".bin", ".dat" };
            }
        }

        public static List<string>? ExtractOptionsFromComment(string? comment)
        {
            if (string.IsNullOrWhiteSpace(comment)) return null;

            // Pattern 1: (en and zh supported) or (en, zh supported)
            var matchSupported = Regex.Match(comment, @"\(([^)]+?)\s+(?:supported|available)\)", RegexOptions.IgnoreCase);
            if (matchSupported.Success)
            {
                var raw = matchSupported.Groups[1].Value;
                var parts = Regex.Split(raw, @"\s*(?:,|and|or|/)\s*", RegexOptions.IgnoreCase)
                                 .Select(s => s.Trim().Trim('"', '\''))
                                 .Where(s => !string.IsNullOrEmpty(s) && !s.Contains(" "))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .ToList();
                if (parts.Count >= 2) return parts;
            }

            // Pattern 2: ( option1 | option2 | ... )
            var matchPipes = Regex.Match(comment, @"\(\s*([a-zA-Z0-9_\-\.]+(?:\s*\|\s*[a-zA-Z0-9_\-\.]+)+)\s*\)");
            if (matchPipes.Success)
            {
                var parts = matchPipes.Groups[1].Value.Split('|')
                                      .Select(s => s.Trim().Trim('"', '\''))
                                      .Where(s => !string.IsNullOrEmpty(s))
                                      .Distinct(StringComparer.OrdinalIgnoreCase)
                                      .ToList();
                if (parts.Count >= 2) return parts;
            }

            // Pattern 3: Options: A, B or Values: A, B
            var matchOptions = Regex.Match(comment, @"(?:options|values)\s*:\s*([^.\r\n]+)", RegexOptions.IgnoreCase);
            if (matchOptions.Success)
            {
                var raw = matchOptions.Groups[1].Value;
                var parts = Regex.Split(raw, @"\s*(?:,|\||/)\s*")
                                 .Select(s => s.Trim().Trim('"', '\''))
                                 .Where(s => !string.IsNullOrEmpty(s) && !s.Contains(" "))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .ToList();
                if (parts.Count >= 2) return parts;
            }

            return null;
        }

        private static string FormatTomlValue(string value, PropertyValueType type)
        {
            switch (type)
            {
                case PropertyValueType.Boolean:
                    var bVal = value.ToLowerInvariant() is "true" or "1" or "yes";
                    return bVal ? "true" : "false";

                case PropertyValueType.Integer:
                case PropertyValueType.Float:
                    return value;

                case PropertyValueType.String:
                case PropertyValueType.FilePath:
                case PropertyValueType.DirectoryPath:
                default:
                    // Quote string
                    var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    return $"\"{escaped}\"";
            }
        }
    }
}
