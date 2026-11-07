using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace sinmai_launcher
{
    public partial class App : Application
    {
        private Window? _window;
        private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "app_diag.txt");

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        public App()
        {
            try { File.Delete(LogPath); } catch { }
            Log("App constructor started");

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Log($"AppDomain UnhandledException: {e.ExceptionObject}");
            };

            this.UnhandledException += (s, e) =>
            {
                Log($"XAML UnhandledException: {e.Message} | {e.Exception}");
            };

            InitializeComponent();
            Log("App InitializeComponent finished");
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            Log("App OnLaunched started");
            try
            {
                Log("Creating MainWindow...");
                _window = new MainWindow();
                _window.Closed += (s, e) => Log("MainWindow Closed event fired!");
                _window.VisibilityChanged += (s, e) => Log($"MainWindow VisibilityChanged: {e.Visible}");
                Log("MainWindow created. Activating...");
                _window.Activate();
                Log("MainWindow.Activate() returned.");
            }
            catch (Exception ex)
            {
                Log($"Exception in OnLaunched: {ex}");
            }
        }
    }
}
