namespace sinmai_launcher.Core.Models
{
    public enum ConsoleOutputMode
    {
        Minimized,
        Show,
        Hidden
    }

    public class LaunchSettings
    {
        public int ScreenWidth { get; set; } = 2160;
        public int ScreenHeight { get; set; } = 1920;
        public bool Fullscreen { get; set; } = false;
        public bool PopupWindow { get; set; } = true;
        public ConsoleOutputMode ConsoleMode { get; set; } = ConsoleOutputMode.Minimized;
        public string? BatFilePath { get; set; }
        public string? AppExePath { get; set; }
        public string? DaemonExePath { get; set; }
        public bool InjectionLocked { get; set; } = true;
        public string ColorPalette { get; set; } = "MaimaiBlue";
        public System.Collections.Generic.List<string> CustomDnsOptions { get; set; } = new();
        public System.Collections.Generic.List<string> CustomAimeDbOptions { get; set; } = new();
        public System.Collections.Generic.List<string> CustomKeychipOptions { get; set; } = new();
    }
}
