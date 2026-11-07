using System.Collections.Generic;

namespace sinmai_launcher.Core.Models
{
    public class ColorPalette
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string BackgroundHex { get; set; } = "#0F172A";
        public string CardBackgroundHex { get; set; } = "#1E293B";
        public string HeaderHex { get; set; } = "#38BDF8";
        public string PrimaryButtonBackgroundHex { get; set; } = "#0284C7";
        public string PrimaryButtonForegroundHex { get; set; } = "#FFFFFF";
        public string AccentBorderHex { get; set; } = "#38BDF8";
        public string StickyHeaderBackgroundHex { get; set; } = "#1E293B";

        public static List<ColorPalette> DefaultPalettes { get; } = new()
        {
            new ColorPalette
            {
                Id = "MaimaiBlue",
                DisplayName = "Maimai DX Blue",
                BackgroundHex = "#0F172A",
                CardBackgroundHex = "#1E293B",
                HeaderHex = "#38BDF8",
                PrimaryButtonBackgroundHex = "#0284C7",
                PrimaryButtonForegroundHex = "#FFFFFF",
                AccentBorderHex = "#0284C7",
                StickyHeaderBackgroundHex = "#1E293B"
            },
            new ColorPalette
            {
                Id = "RaspberryPink",
                DisplayName = "Raspberry DX",
                BackgroundHex = "#1A0B1A",
                CardBackgroundHex = "#2D122D",
                HeaderHex = "#FB7185",
                PrimaryButtonBackgroundHex = "#E11D48",
                PrimaryButtonForegroundHex = "#FFFFFF",
                AccentBorderHex = "#FB7185",
                StickyHeaderBackgroundHex = "#2D122D"
            },
            new ColorPalette
            {
                Id = "OngekiSunset",
                DisplayName = "Ongeki Sunset",
                BackgroundHex = "#1A140C",
                CardBackgroundHex = "#2B1F11",
                HeaderHex = "#FB923C",
                PrimaryButtonBackgroundHex = "#EA580C",
                PrimaryButtonForegroundHex = "#FFFFFF",
                AccentBorderHex = "#FB923C",
                StickyHeaderBackgroundHex = "#2B1F11"
            },
            new ColorPalette
            {
                Id = "CyberNeon",
                DisplayName = "Cyber Neon",
                BackgroundHex = "#0D0B18",
                CardBackgroundHex = "#1A162B",
                HeaderHex = "#C084FC",
                PrimaryButtonBackgroundHex = "#9333EA",
                PrimaryButtonForegroundHex = "#FFFFFF",
                AccentBorderHex = "#A855F7",
                StickyHeaderBackgroundHex = "#1A162B"
            },
            new ColorPalette
            {
                Id = "EmeraldMint",
                DisplayName = "Emerald Mint",
                BackgroundHex = "#081A14",
                CardBackgroundHex = "#0F2E23",
                HeaderHex = "#34D399",
                PrimaryButtonBackgroundHex = "#059669",
                PrimaryButtonForegroundHex = "#FFFFFF",
                AccentBorderHex = "#34D399",
                StickyHeaderBackgroundHex = "#0F2E23"
            }
        };

        public static ColorPalette GetPaletteOrDefault(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return DefaultPalettes[0];
            return DefaultPalettes.Find(p => p.Id.Equals(id, System.StringComparison.OrdinalIgnoreCase)) ?? DefaultPalettes[0];
        }
    }
}
