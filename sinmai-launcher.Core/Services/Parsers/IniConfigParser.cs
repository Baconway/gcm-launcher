using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using sinmai_launcher.Core.Models;

namespace sinmai_launcher.Core.Services.Parsers
{
    public class IniConfigParser
    {
        public ConfigDocument Parse(string content, string filePath, ConfigFormat format = ConfigFormat.Ini)
        {
            var document = new ConfigDocument
            {
                FilePath = filePath,
                Format = format
            };

            var currentSection = new ConfigSection { Name = "General" };
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
                if (trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                {
                    currentComments.AppendLine(trimmed);
                    continue;
                }

                // Section header
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    hasEncounteredSection = true;
                    // If current section has properties, keep it
                    if (currentSection.Properties.Count > 0 || currentSection.Name != "General")
                    {
                        document.Sections.Add(currentSection);
                    }

                    var sectionName = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    currentSection = new ConfigSection
                    {
                        Name = sectionName,
                        Comment = currentComments.Length > 0 ? currentComments.ToString().TrimEnd() : null
                    };
                    currentComments.Clear();
                    continue;
                }

                // Key = Value
                var equalsIdx = trimmed.IndexOf('=');
                if (equalsIdx > 0)
                {
                    var key = trimmed.Substring(0, equalsIdx).Trim();
                    var value = trimmed.Substring(equalsIdx + 1).Trim();

                    // Strip inline comment if exists
                    string? inlineComment = null;
                    var semiIdx = value.IndexOf(';');
                    if (semiIdx >= 0)
                    {
                        inlineComment = value.Substring(semiIdx).Trim();
                        value = value.Substring(0, semiIdx).Trim();
                    }

                    var commentText = currentComments.Length > 0 ? currentComments.ToString().TrimEnd() : null;
                    if (!string.IsNullOrEmpty(inlineComment))
                    {
                        commentText = string.IsNullOrEmpty(commentText) ? inlineComment : commentText + " " + inlineComment;
                    }

                    var prop = new ConfigProperty
                    {
                        Key = key,
                        RawValue = value,
                        Comment = commentText,
                        ValueType = InferValueType(key, value)
                    };

                    ApplySchemaRules(prop, currentSection.Name);
                    currentSection.Properties.Add(prop);
                    currentComments.Clear();
                }
            }

            if (currentSection.Properties.Count > 0 || !document.Sections.Contains(currentSection))
            {
                document.Sections.Add(currentSection);
            }

            document.HeaderComment = headerComments.Length > 0 ? headerComments.ToString().TrimEnd() : null;

            if (Path.GetFileName(filePath).Equals("segatools.ini", StringComparison.OrdinalIgnoreCase))
            {
                PostProcessSegatools(document);
            }

            return document;
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
                if (section.Name != "General" || document.Sections.Count > 1)
                {
                    if (!string.IsNullOrEmpty(section.Comment))
                    {
                        sb.AppendLine(section.Comment);
                    }
                    sb.AppendLine($"[{section.Name}]");
                }

                foreach (var prop in section.Properties)
                {
                    if (prop.Key.Equals("aimeDB", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(prop.RawValue) || prop.RawValue.Equals("(Disabled)", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.IsNullOrEmpty(prop.Comment))
                            {
                                sb.AppendLine(prop.Comment);
                            }
                            sb.AppendLine(";AimeDB=");
                            continue;
                        }
                    }

                    if (!string.IsNullOrEmpty(prop.Comment))
                    {
                        sb.AppendLine(prop.Comment);
                    }
                    sb.AppendLine($"{prop.Key}={prop.RawValue}");
                }

                sb.AppendLine();
            }

            return sb.ToString().TrimEnd() + Environment.NewLine;
        }

        public static PropertyValueType InferValueType(string key, string value)
        {
            var lowerKey = key.ToLowerInvariant();
            var lowerVal = value.ToLowerInvariant();

            // Known booleans
            if (lowerVal is "1" or "0" or "true" or "false" or "yes" or "no" or "on" or "off")
            {
                // Keys that contain 'id' or 'port' or 'serial' might be numeric or strings rather than bools
                if (lowerKey.Contains("id") || lowerKey.Contains("port") || lowerKey.Contains("serial") || lowerKey.Contains("track"))
                {
                    return PropertyValueType.Integer;
                }
                return PropertyValueType.Boolean;
            }

            // Path keys
            if (lowerKey.Contains("path") || lowerKey.Contains("file") || lowerKey.Contains("dir") || value.Contains("\\") || value.Contains("/"))
            {
                return PropertyValueType.FilePath;
            }

            // Integer
            if (long.TryParse(value, out _))
            {
                return PropertyValueType.Integer;
            }

            // Float
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

            var extracted = TomlConfigParser.ExtractOptionsFromComment(prop.Comment);
            if (extracted != null && extracted.Count >= 2)
            {
                prop.Options = extracted;
            }

            if (lowerKey == "target" && lowerSection == "am")
            {
                prop.Options ??= new List<string> { "0", "1" };
            }
            else if (lowerKey == "sound8ch")
            {
                prop.Options ??= new List<string> { "0", "1" };
            }
            else if (lowerSection is "dns")
            {
                if (lowerKey is "default" or "dns")
                {
                    prop.AllowCustomOptions = true;
                    prop.Options ??= new List<string> { "sega.vantablack.io.vn", "auth.sys.all-net.ariidesu.moe", "127.0.0.1" };
                }
                else if (lowerKey is "aimedb")
                {
                    prop.IsOptional = true;
                    prop.AllowCustomOptions = true;
                    prop.Options ??= new List<string> { "(Disabled)", "aime.naominet.jp", "aime.vantablack.io.vn", "127.0.0.1" };
                }
            }
            else if (lowerSection is "keychip")
            {
                if (lowerKey is "id" or "keychip")
                {
                    prop.AllowCustomOptions = true;
                    prop.Options ??= new List<string> { "A63E-01E05480000", "A69E-01A88888888", "A63E-01A88888888" };
                }
            }
        }

        private static readonly Dictionary<string, string> SegatoolsBaseSectionComments = new(StringComparer.OrdinalIgnoreCase)
        {
            ["vfs"] = "Path settings",
            ["aime"] = "Device settings for Aime card reader assembly emulation",
            ["vfd"] = "VFD GP1232A02A FUTABA assembly emulation settings",
            ["dns"] = "Network settings for server routing and DNS",
            ["netenv"] = "Simulate an ideal LAN environment for arcade head-to-head and network play",
            ["keychip"] = "Board keychip serial and LAN subnet settings",
            ["pcbid"] = "Windows host name ALLS MAIN ID setting",
            ["system"] = "ALLS system, freeplay, and LAN install settings",
            ["led15070"] = "837-15070-04 controlled cabinet and button LEDs",
            ["unity"] = "Unity hook settings for running custom .NET code or BepInEx",
            ["aimeio"] = "Custom card reader IO DLL settings",
            ["mai2io"] = "Custom maimai DX IO DLL settings",
            ["io4"] = "Input settings for test, service, and coin buttons",
            ["button"] = "Key bindings for buttons around screen",
            ["touch"] = "Touch panel emulation settings",
            ["gfx"] = "Graphics hook and window settings",
            ["led15093"] = "837-15093-06 controlled air tower RGBs and billboard LEDs",
            ["led"] = "LED serial and named pipe output settings",
            ["chuniio"] = "Custom CHUNITHM IO DLL settings",
            ["io3"] = "Input settings for test, service, coin, and IR buttons",
            ["ir"] = "Infrared sensor controller settings",
            ["slider"] = "Slider touch strip settings"
        };

        private static readonly Dictionary<(string Section, string Key), string> SegatoolsBaseComments = new()
        {
            { ("vfs", "amfs"), "Insert the path to the game AMFS directory here (contains ICF1 and ICF2)" },
            { ("vfs", "option"), "Insert the path to the game Option directory here (contains Axxx directories)" },
            { ("vfs", "appdata"), "Create an empty directory somewhere and insert the path here. This directory may be shared between multiple SEGA games. NOTE: This has nothing to do with Windows %APPDATA%." },
            { ("aime", "enable"), "Enable Aime card reader assembly emulation. Disable to use a real SEGA Aime reader." },
            { ("aime", "aimePath"), "Path to aime.txt" },
            { ("aime", "scan"), "Virtual-key code. If this button is held then the emulated IC card reader emulates an IC card in its proximity." },
            { ("vfd", "enable"), "Enable VFD emulation. Disable to use a real VFD GP1232A02A FUTABA assembly." },
            { ("dns", "default"), "Insert the hostname or IP address of the server you wish to use here. Note that 127.0.0.1, localhost etc are specifically rejected." },
            { ("dns", "dns"), "Insert the hostname or IP address of the server you wish to use here." },
            { ("dns", "aimedb"), "Insert the hostname or IP address of the Aime DB server. Optional." },
            { ("netenv", "enable"), "Simulate an ideal LAN environment. This may interfere with head-to-head play. SEGA games are somewhat picky about its LAN environment, so leaving this setting enabled is recommended." },
            { ("netenv", "addrSuffix"), "The final octet of the local host's IP address on the virtualized subnet." },
            { ("keychip", "id"), "Keychip serial number. Keychip serials observed in the wild follow this pattern: A\\d{2}(E|X)-(01|20)[ABCDU]\\d{8}." },
            { ("keychip", "subnet"), "The /24 LAN subnet that the emulated keychip will tell the game to expect. If you disable netenv then you must set this to your LAN's IP subnet, and that subnet must start with 192.168." },
            { ("pcbid", "serialNo"), "Set the Windows host name. This should be an ALLS MAIN ID, without the hyphen (which is not a valid character in a Windows host name)." },
            { ("system", "enable"), "Enable ALLS system settings." },
            { ("system", "freeplay"), "Enable freeplay mode. This will disable the coin slot and set the game to freeplay." },
            { ("system", "dipsw1"), "LAN Install: If multiple machines are present on the same LAN then set this to 1 on exactly one machine and set this to 0 on all others." },
            { ("system", "dipsw2"), "Monitor type: 0 = 120FPS, 1 = 60FPS" },
            { ("system", "dipsw3"), "Cab type: 0 = SP, 1 = CVT. SP will enable VFD and eMoney." },
            { ("led15070", "enable"), "Enable emulation of the 837-15070-04 controlled lights, which handle the cabinet and button LEDs." },
            { ("unity", "enable"), "Enable Unity hook. This will allow you to run custom .NET code before the game." },
            { ("unity", "targetAssembly"), "Path to a .NET DLL that should run before the game. Useful for loading modding frameworks such as BepInEx." },
            { ("aimeio", "path"), "To use a custom card reader IO DLL enter its path here. Leave empty if you want to use Segatools built-in keyboard input." },
            { ("mai2io", "path"), "To use a custom maimai DX IO DLL enter its path here. Leave empty if you want to use Segatools built-in keyboard input." },
            { ("io4", "test"), "Test button virtual-key code. Default is the F1 key (0x70)." },
            { ("io4", "service"), "Service button virtual-key code. Default is the F2 key (0x71)." },
            { ("io4", "coin"), "Keyboard button to increment coin counter. Default is the F3 key (0x72)." },
            { ("button", "enable"), "Key bindings for buttons around screen." },
            { ("touch", "p1Enable"), "Enable Player 1 touch emulation." },
            { ("touch", "p2Enable"), "Enable Player 2 touch emulation." },
            { ("gfx", "enable"), "Enables the graphics hook." },
            { ("gfx", "windowed"), "Force the game to run windowed." },
            { ("gfx", "framed"), "Add a frame to the game window if running windowed." },
            { ("gfx", "monitor"), "Select the monitor to run the game on. (Fullscreen only, 0 = primary screen)" },
            { ("gfx", "dpiAware"), "Enable DPI awareness for the game process, preventing Windows from stretching the game window if a DPI scaling higher than 100% is used." },
            { ("led15093", "enable"), "Enable emulation of the 15093-06 controlled lights, which handle the air tower RGBs and the rear LED panel (billboard) on the cabinet." },
            { ("led", "cabLedOutputPipe"), "Output billboard LED strip data to a named pipe called \\\\.\\pipe\\chuni_led" },
            { ("led", "cabLedOutputSerial"), "Output billboard LED strip data to serial" },
            { ("led", "controllerLedOutputPipe"), "Output slider LED data to the named pipe" },
            { ("led", "controllerLedOutputSerial"), "Output slider LED data to the serial port" },
            { ("led", "controllerLedOutputOpeNITHM"), "Use the OpeNITHM protocol for serial LED output" },
            { ("chuniio", "path"), "Custom chuniio 32-bit DLL path." },
            { ("chuniio", "path32"), "Custom chuniio 32-bit DLL path." },
            { ("chuniio", "path64"), "Custom chuniio 64-bit DLL path." },
            { ("io3", "test"), "Test button virtual-key code. Default is the F1 key (0x70)." },
            { ("io3", "service"), "Service button virtual-key code. Default is the F2 key (0x71)." },
            { ("io3", "coin"), "Keyboard button to increment coin counter. Default is the F3 key (0x72)." },
            { ("io3", "ir"), "Set to 0 for enable separate IR control. Default is space key (0x20)." },
            { ("slider", "enable"), "Enable slider emulation. If you have real AC slider, set this to 0. Slider serial port must be COM1." }
        };

        private static void PostProcessSegatools(ConfigDocument document)
        {
            // Load live comments from the base file at C:\Users\Baconway\Downloads\segatools.ini if present
            var dynamicComments = new Dictionary<(string Section, string Key), string>();
            var dynamicSectionComments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            const string baseFilePath = @"C:\Users\Baconway\Downloads\segatools.ini";
            if (File.Exists(baseFilePath))
            {
                try
                {
                    var baseLines = File.ReadAllLines(baseFilePath);
                    string? currentSec = null;
                    var commentAcc = new StringBuilder();

                    foreach (var rawLine in baseLines)
                    {
                        var line = rawLine.Trim();
                        if (string.IsNullOrEmpty(line))
                        {
                            commentAcc.Clear();
                            continue;
                        }

                        if (line.StartsWith(";") || line.StartsWith("#"))
                        {
                            var clean = line.TrimStart(';', '#', ' ', '\t');
                            if (!clean.StartsWith("---"))
                            {
                                if (commentAcc.Length > 0) commentAcc.Append(" ");
                                commentAcc.Append(clean);
                            }
                            continue;
                        }

                        if (line.StartsWith("[") && line.EndsWith("]"))
                        {
                            currentSec = line.Substring(1, line.Length - 2).Trim();
                            if (commentAcc.Length > 0)
                            {
                                dynamicSectionComments[currentSec] = commentAcc.ToString().Trim();
                                commentAcc.Clear();
                            }
                            continue;
                        }

                        var eqIdx = line.IndexOf('=');
                        if (eqIdx > 0 && currentSec != null)
                        {
                            var k = line.Substring(0, eqIdx).Trim();
                            if (commentAcc.Length > 0)
                            {
                                dynamicComments[(currentSec.ToLowerInvariant(), k.ToLowerInvariant())] = commentAcc.ToString().Trim();
                                commentAcc.Clear();
                            }
                        }
                    }
                }
                catch { }
            }

            // Apply comments to all sections & properties
            foreach (var sec in document.Sections)
            {
                var lowerSec = sec.Name.ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(sec.Comment))
                {
                    if (dynamicSectionComments.TryGetValue(sec.Name, out var dynSecComm))
                    {
                        sec.Comment = dynSecComm;
                    }
                    else if (SegatoolsBaseSectionComments.TryGetValue(sec.Name, out var baseSecComm))
                    {
                        sec.Comment = baseSecComm;
                    }
                }

                foreach (var prop in sec.Properties)
                {
                    var lowerKey = prop.Key.ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(prop.Comment))
                    {
                        if (dynamicComments.TryGetValue((lowerSec, lowerKey), out var dynComm))
                        {
                            prop.Comment = dynComm;
                        }
                        else
                        {
                            var match = SegatoolsBaseComments.FirstOrDefault(kvp =>
                                kvp.Key.Section.Equals(lowerSec, StringComparison.OrdinalIgnoreCase) &&
                                kvp.Key.Key.Equals(lowerKey, StringComparison.OrdinalIgnoreCase));
                            if (!string.IsNullOrEmpty(match.Value))
                            {
                                prop.Comment = match.Value;
                            }
                        }
                    }
                }
            }

            // Ensure [DNS] section has default and optional aimeDB
            var dnsSection = document.Sections.FirstOrDefault(s => s.Name.Equals("dns", StringComparison.OrdinalIgnoreCase));
            if (dnsSection != null)
            {
                var defaultProp = dnsSection.Properties.FirstOrDefault(p => p.Key.Equals("default", StringComparison.OrdinalIgnoreCase) || p.Key.Equals("dns", StringComparison.OrdinalIgnoreCase));
                if (defaultProp != null)
                {
                    defaultProp.AllowCustomOptions = true;
                    defaultProp.Options ??= new List<string> { "sega.vantablack.io.vn", "auth.sys.all-net.ariidesu.moe", "127.0.0.1" };
                    if (!string.IsNullOrEmpty(defaultProp.RawValue) && !defaultProp.Options.Contains(defaultProp.RawValue, StringComparer.OrdinalIgnoreCase))
                    {
                        defaultProp.Options.Insert(0, defaultProp.RawValue);
                    }
                }

                var aimeDbProp = dnsSection.Properties.FirstOrDefault(p => p.Key.Equals("aimedb", StringComparison.OrdinalIgnoreCase));
                if (aimeDbProp == null)
                {
                    aimeDbProp = new ConfigProperty
                    {
                        Key = "AimeDB",
                        RawValue = "(Disabled)",
                        ValueType = PropertyValueType.String,
                        Comment = "Insert the hostname or IP address of the Aime DB server. Optional.",
                        IsOptional = true,
                        AllowCustomOptions = true,
                        Options = new List<string> { "(Disabled)", "aime.naominet.jp", "aime.vantablack.io.vn", "127.0.0.1" }
                    };
                    dnsSection.Properties.Add(aimeDbProp);
                }
                else
                {
                    aimeDbProp.IsOptional = true;
                    aimeDbProp.AllowCustomOptions = true;
                    aimeDbProp.Options ??= new List<string> { "(Disabled)", "aime.naominet.jp", "aime.vantablack.io.vn", "127.0.0.1" };
                    if (!aimeDbProp.Options.Contains("(Disabled)"))
                    {
                        aimeDbProp.Options.Insert(0, "(Disabled)");
                    }
                    if (string.IsNullOrWhiteSpace(aimeDbProp.RawValue))
                    {
                        aimeDbProp.RawValue = "(Disabled)";
                    }
                }
            }

            // Ensure [KeyChip] section has id property with custom options
            var keychipSection = document.Sections.FirstOrDefault(s => s.Name.Equals("keychip", StringComparison.OrdinalIgnoreCase));
            if (keychipSection != null)
            {
                var idProp = keychipSection.Properties.FirstOrDefault(p => p.Key.Equals("id", StringComparison.OrdinalIgnoreCase) || p.Key.Equals("keychip", StringComparison.OrdinalIgnoreCase));
                if (idProp != null)
                {
                    idProp.AllowCustomOptions = true;
                    idProp.Options ??= new List<string> { "A63E-01E05480000", "A69E-01A88888888", "A63E-01A88888888" };
                    if (!string.IsNullOrEmpty(idProp.RawValue) && !idProp.Options.Contains(idProp.RawValue, StringComparer.OrdinalIgnoreCase))
                    {
                        idProp.Options.Insert(0, idProp.RawValue);
                    }
                }
            }
        }
    }
}
