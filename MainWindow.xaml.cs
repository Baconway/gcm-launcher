using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage.Pickers;
using sinmai_launcher.Core.Models;
using sinmai_launcher.Core.Services;

namespace sinmai_launcher
{
    public class ConfigFileItem
    {
        public string FileName
        {
            get
            {
                var name = Path.GetFileName(FullPath);
                if (!string.IsNullOrEmpty(RelativePath))
                {
                    if (RelativePath.Contains("Sinmai-Assist", StringComparison.OrdinalIgnoreCase))
                    {
                        return Path.Combine("Sinmai-Assist", name);
                    }
                    if (RelativePath.Contains("DEVICE", StringComparison.OrdinalIgnoreCase))
                    {
                        return Path.Combine("DEVICE", name);
                    }
                }
                return name;
            }
        }
        public string RelativePath { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string Format => Path.GetExtension(FullPath).TrimStart('.').ToUpperInvariant();
    }

    public sealed partial class MainWindow : Window
    {
        private readonly IProfileService _profileService;
        private readonly IGameScannerService _scannerService;
        private readonly IValidationService _validationService;
        private readonly IConfigService _configService;
        private readonly ILaunchService _launchService;

        private Profile? _activeProfile;
        private List<Profile> _allProfiles = new();
        private ConfigDocument? _currentConfigDoc;
        private bool _isEditingProfile = false;
        private string? _editingProfileId = null;

        // UI tracking for property edits
        private readonly Dictionary<ConfigProperty, FrameworkElement> _propertyControls = new();

        public class TocItem
        {
            public string DisplayName { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public ConfigSection Section { get; set; } = null!;
            public FrameworkElement Container { get; set; } = null!;
        }

        private class SectionViewItem
        {
            public ConfigSection Section { get; set; } = null!;
            public FrameworkElement Container { get; set; } = null!;
            public FrameworkElement HeaderPanel { get; set; } = null!;
            public List<(ConfigProperty Prop, FrameworkElement Row)> Properties { get; set; } = new();
        }

        private readonly List<SectionViewItem> _sectionViews = new();
        private bool _isFileBrowserVisible = true;
        private bool _isProgrammaticTocChange = false;
        private List<ConfigFileItem> _discoveredConfigs = new();
        private ColorPalette _currentPalette = ColorPalette.DefaultPalettes[0];

        public MainWindow()
        {
            App.Log("MainWindow constructor started");
            try
            {
                App.Log("Calling MainWindow InitializeComponent...");
                InitializeComponent();
                App.Log("MainWindow InitializeComponent finished");

                ConfigureWindowLayout();

                _validationService = new ValidationService();
                _configService = new ConfigService(_validationService);
                _scannerService = new GameScannerService();
                _launchService = new LaunchService();
                _profileService = new ProfileService();

                _launchService.GameStateChanged += LaunchService_GameStateChanged;

                if (Content is FrameworkElement root)
                {
                    App.Log("Subscribing to root.Loaded");
                    root.Loaded += async (s, e) =>
                    {
                        App.Log("root.Loaded event fired!");
                        try
                        {
                            WireEventHandlers();
                            await InitializeAppAsync();
                            App.Log("InitializeAppAsync finished successfully");
                        }
                        catch (Exception ex)
                        {
                            App.Log($"Error in root.Loaded: {ex}");
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                App.Log($"Exception in MainWindow constructor: {ex}");
                throw;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private void ConfigureWindowLayout()
        {
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                App.Log($"Window HWND: 0x{hWnd:X}");
                var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);
                if (appWindow != null)
                {
                    appWindow.Title = "sinmai-launcher";

                    var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
                    if (!File.Exists(iconPath))
                    {
                        iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
                    }
                    if (File.Exists(iconPath))
                    {
                        try
                        {
                            appWindow.SetIcon(iconPath);
                        }
                        catch (Exception ex)
                        {
                            App.Log($"Could not set app window icon: {ex.Message}");
                        }
                    }

                    appWindow.Resize(new SizeInt32(1280, 840));

                    var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                    if (displayArea != null)
                    {
                        var centered = appWindow.Position;
                        centered.X = Math.Max(0, (displayArea.WorkArea.Width - 1150) / 2);
                        centered.Y = Math.Max(0, (displayArea.WorkArea.Height - 800) / 2);
                        appWindow.Move(centered);
                    }
                    appWindow.Show(activateWindow: true);
                    App.Log($"AppWindow configured: 1150x800, visible={appWindow.IsVisible}");
                }

                if (hWnd != IntPtr.Zero)
                {
                    ShowWindow(hWnd, 5); // SW_SHOW
                    SetForegroundWindow(hWnd);
                }
            }
            catch (Exception ex)
            {
                App.Log($"Error configuring window layout: {ex.Message}");
            }
        }

        private void WireEventHandlers()
        {
            ProfileSelectorCombo.SelectionChanged += ProfileSelectorCombo_SelectionChanged;
            PaletteSelectorCombo.SelectionChanged += PaletteSelectorCombo_SelectionChanged;

            // Launch settings inputs: real-time application to launch script
            ResolutionPresetCombo.SelectionChanged += ResolutionPresetCombo_SelectionChanged;
            ScreenWidthBox.ValueChanged += async (s, e) =>
            {
                ResolutionValue_Changed(s, e);
                await ApplyLaunchSettingsToScriptAsync();
            };
            ScreenHeightBox.ValueChanged += async (s, e) =>
            {
                ResolutionValue_Changed(s, e);
                await ApplyLaunchSettingsToScriptAsync();
            };
            FullscreenSwitch.Toggled += async (s, e) => await ApplyLaunchSettingsToScriptAsync();
            PopupWindowSwitch.Toggled += async (s, e) => await ApplyLaunchSettingsToScriptAsync();
            ConsoleModeCombo.SelectionChanged += async (s, e) => await ApplyLaunchSettingsToScriptAsync();

            if (GameRadioMaimai != null)
            {
                GameRadioMaimai.Checked += GameRadio_Checked;
            }
            if (GameRadioChunithm != null)
            {
                GameRadioChunithm.Checked += GameRadio_Checked;
            }

            GameDirInput.TextChanged += GameDirInput_TextChanged;
            AvatarPathInput.TextChanged += AvatarPathInput_TextChanged;

            if (ConfigTocBorder != null)
            {
                ConfigTocBorder.PointerEntered += ConfigTocBorder_PointerEntered;
                ConfigTocBorder.PointerExited += ConfigTocBorder_PointerExited;
            }

            SetTocVisibility(false);
        }

        private async Task InitializeAppAsync()
        {
            try
            {
                if (PaletteSelectorCombo != null)
                {
                    PaletteSelectorCombo.ItemsSource = ColorPalette.DefaultPalettes;
                    PaletteSelectorCombo.DisplayMemberPath = "DisplayName";
                }

                _allProfiles = await _profileService.LoadProfilesAsync();
                if (!_allProfiles.Any())
                {
                    ShowStartupOverlay(isEditing: false);
                    // Check if default ga 1.65 directory exists and pre-populate
                    const string defaultGaDir = @"C:\Users\Baconway\Downloads\ga 1.65";
                    if (Directory.Exists(defaultGaDir))
                    {
                        GameDirInput.Text = defaultGaDir;
                        await ScanGameDirectoryAsync(defaultGaDir, GameType.Maimai);
                    }
                }
                else
                {
                    StartupOverlay.Visibility = Visibility.Collapsed;
                    DashboardRoot.Visibility = Visibility.Visible;
                    _activeProfile = await _profileService.GetActiveProfileAsync() ?? _allProfiles.First();
                    RefreshProfileSelector();
                    await LoadActiveProfileUIAsync();
                }
            }
            catch (Exception ex)
            {
                ShowNotification("Initialization Error", ex.Message, InfoBarSeverity.Error);
            }
        }

        private void RefreshProfileSelector()
        {
            if (ProfileSelectorCombo == null) return;

            ProfileSelectorCombo.ItemsSource = null;
            ProfileSelectorCombo.ItemsSource = _allProfiles.Select(p => p.Name).ToList();
            if (_activeProfile != null)
            {
                var idx = _allProfiles.FindIndex(p => p.Id == _activeProfile.Id);
                if (idx >= 0)
                {
                    ProfileSelectorCombo.SelectedIndex = idx;
                }
            }
        }

        private async Task LoadActiveProfileUIAsync()
        {
            if (_activeProfile == null) return;

            // Header profile display (Filepaths hidden per FEEDBACK.md)
            HeaderProfileName.Text = _activeProfile.Name;
            HeaderGameBadge.Text = _activeProfile.Game.GetDisplayName();
            HeaderProfilePic.DisplayName = _activeProfile.Name;

            // Apply selected color palette
            var paletteId = _activeProfile.LaunchSettings.ColorPalette ?? "MaimaiBlue";
            var selectedPalette = ColorPalette.DefaultPalettes.FirstOrDefault(p => p.Id == paletteId) ?? ColorPalette.DefaultPalettes[0];
            if (PaletteSelectorCombo != null)
            {
                PaletteSelectorCombo.SelectedItem = selectedPalette;
            }
            ApplyColorPalette(selectedPalette);

            if (!string.IsNullOrEmpty(_activeProfile.ProfilePicturePath) && File.Exists(_activeProfile.ProfilePicturePath))
            {
                try
                {
                    HeaderProfilePic.ProfilePicture = new BitmapImage(new Uri(_activeProfile.ProfilePicturePath));
                }
                catch
                {
                    HeaderProfilePic.ProfilePicture = null;
                }
            }
            else
            {
                HeaderProfilePic.ProfilePicture = null;
            }

            // Launch Settings tab
            var s = _activeProfile.LaunchSettings;
            _isUpdatingLaunchSettingsUI = true;
            try
            {
                ScreenWidthBox.Value = s.ScreenWidth;
                ScreenHeightBox.Value = s.ScreenHeight;
                FullscreenSwitch.IsOn = s.Fullscreen;
                PopupWindowSwitch.IsOn = s.PopupWindow;
                ConsoleModeCombo.SelectedIndex = (int)s.ConsoleMode;
                UpdateResolutionPresetSelection(s.ScreenWidth, s.ScreenHeight);
            }
            finally
            {
                _isUpdatingLaunchSettingsUI = false;
            }

            // Overview tab (The only place where filepaths are shown as Information section per FEEDBACK.md)
            OverviewGameTitle.Text = _activeProfile.Game.GetDisplayName();
            OverviewRootDir.Text = _activeProfile.GameDirectory;
            OverviewExePath.Text = s.AppExePath ?? "Not detected";
            OverviewBatPath.Text = s.BatFilePath ?? "Not detected";

            // Scan directory for configs and mods
            if (Directory.Exists(_activeProfile.GameDirectory))
            {
                var scan = await _scannerService.ScanGameFolderAsync(_activeProfile.GameDirectory, _activeProfile.Game);
                
                // Update detected files in list (deduplicating by filename to strictly prevent any duplicate items)
                var items = scan.DetectedConfigFiles
                    .Select(f => new ConfigFileItem
                    {
                        FullPath = f,
                        RelativePath = GetRelativePathSafe(_activeProfile.GameDirectory, f)
                    })
                    .GroupBy(item => Path.GetFileName(item.FullPath), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                _discoveredConfigs = items;
                ConfigFilesListView.ItemsSource = items;
                if (items.Any())
                {
                    ConfigFilesListView.SelectedIndex = 0;
                }
                else
                {
                    ActiveConfigTitle.Text = "No configuration files detected";
                    ActiveConfigSubtext.Text = string.Empty;
                    ConfigPropertiesContainer.Children.Clear();
                    RawConfigTextBox.Text = string.Empty;
                    SetTocVisibility(false);
                    if (StickyHeaderBar != null) StickyHeaderBar.Visibility = Visibility.Collapsed;
                }

                // Update mods list
                DetectedModsItemsControl.ItemsSource = scan.DetectedMods;

                if (scan.AppExePath != null) s.AppExePath = scan.AppExePath;
                if (scan.BatFilePath != null) s.BatFilePath = scan.BatFilePath;
                if (scan.DaemonExePath != null) s.DaemonExePath = scan.DaemonExePath;

                // Validate configuration correctness for Ready to Launch status
                await CheckAndVerifyConfigStatusAsync();
            }
        }

        private static string GetRelativePathSafe(string baseDir, string fullPath)
        {
            try
            {
                return Path.GetRelativePath(baseDir, fullPath);
            }
            catch
            {
                return Path.GetFileName(fullPath);
            }
        }

        #region Game Launch & Process Lifecycle

        private async void LaunchGameBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_activeProfile == null) return;

            LaunchGameBtn.IsEnabled = false;
            LaunchProgressRing.Visibility = Visibility.Visible;
            LaunchProgressRing.IsActive = true;
            StatusText.Text = "Launching game...";

            await ApplyLaunchSettingsToScriptAsync();

            var result = await _launchService.LaunchGameAsync(_activeProfile);
            if (!result.Success)
            {
                LaunchProgressRing.IsActive = false;
                LaunchProgressRing.Visibility = Visibility.Collapsed;
                LaunchGameBtn.IsEnabled = true;
                StatusText.Text = "Ready to Launch";
                ShowNotification("Launch Failed", result.ErrorMessage ?? "Unknown error launching game.", InfoBarSeverity.Error);
            }
            else
            {
                ShowNotification("Game Started", $"Process started successfully (PID: {result.ProcessId}).", InfoBarSeverity.Success);
            }
        }

        private void StopGameBtn_Click(object sender, RoutedEventArgs e)
        {
            _launchService.StopGame();
            ShowNotification("Game Stopped", "The game process tree has been terminated.", InfoBarSeverity.Informational);
        }

        private void LaunchService_GameStateChanged(object? sender, bool isRunning)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (isRunning)
                {
                    LaunchProgressRing.IsActive = false;
                    LaunchProgressRing.Visibility = Visibility.Collapsed;
                    LaunchGameBtn.Visibility = Visibility.Collapsed;
                    StopGameBtn.Visibility = Visibility.Visible;
                    StatusText.Text = "Game Running";
                    StatusText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 65));
                    StatusDot.Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 65));
                }
                else
                {
                    LaunchProgressRing.IsActive = false;
                    LaunchProgressRing.Visibility = Visibility.Collapsed;
                    LaunchGameBtn.Visibility = Visibility.Visible;
                    LaunchGameBtn.IsEnabled = true;
                    StopGameBtn.Visibility = Visibility.Collapsed;
                    _ = CheckAndVerifyConfigStatusAsync();
                }
            });
        }

        private async Task CheckAndVerifyConfigStatusAsync()
        {
            if (_activeProfile == null) return;

            try
            {
                var configPaths = _discoveredConfigs.Select(c => c.FullPath).ToList();
                if (configPaths.Count == 0 && Directory.Exists(_activeProfile.GameDirectory))
                {
                    var scan = await _scannerService.ScanGameFolderAsync(_activeProfile.GameDirectory, _activeProfile.Game);
                    configPaths = scan.DetectedConfigFiles;
                }

                var summary = await _configService.ValidateGameConfigurationsAsync(_activeProfile.GameDirectory, configPaths);

                if (summary.IsValid)
                {
                    StatusDot.Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 65));
                    StatusText.Text = "Ready to Launch";
                    StatusText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 65));
                    ToolTipService.SetToolTip(LaunchStatusBadge, $"All {summary.ValidFiles.Count} configuration files validated successfully. Ready to run.");
                    LaunchGameBtn.IsEnabled = true;
                }
                else
                {
                    StatusDot.Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 216, 59, 1));
                    StatusText.Text = "Config Check Warning";
                    StatusText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 216, 59, 1));
                    ToolTipService.SetToolTip(LaunchStatusBadge, "Configuration issues detected:" + Environment.NewLine + string.Join(Environment.NewLine, summary.Errors));
                }
            }
            catch (Exception ex)
            {
                StatusDot.Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 216, 59, 1));
                StatusText.Text = "Config Error";
                StatusText.Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 216, 59, 1));
                ToolTipService.SetToolTip(LaunchStatusBadge, "Error validating configuration files: " + ex.Message);
            }
        }

        #region Color Palette Engine

        private Windows.UI.Color ColorFromHex(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                return Windows.UI.Color.FromArgb(255, r, g, b);
            }
            else if (hex.Length == 8)
            {
                byte a = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte r = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
                return Windows.UI.Color.FromArgb(a, r, g, b);
            }
            return Windows.UI.Color.FromArgb(255, 15, 23, 42);
        }

        private void ApplyColorPalette(ColorPalette palette)
        {
            _currentPalette = palette;
            var bgBrush = new SolidColorBrush(ColorFromHex(palette.BackgroundHex));
            var cardBrush = new SolidColorBrush(ColorFromHex(palette.CardBackgroundHex));
            var headerBrush = new SolidColorBrush(ColorFromHex(palette.HeaderHex));
            var btnBgBrush = new SolidColorBrush(ColorFromHex(palette.PrimaryButtonBackgroundHex));
            var btnFgBrush = new SolidColorBrush(ColorFromHex(palette.PrimaryButtonForegroundHex));
            var stickyBgBrush = new SolidColorBrush(ColorFromHex(palette.StickyHeaderBackgroundHex));

            if (DashboardRoot != null)
            {
                DashboardRoot.Background = bgBrush;
            }

            if (HeaderProfileName != null)
            {
                HeaderProfileName.Foreground = headerBrush;
            }

            if (LaunchGameBtn != null)
            {
                LaunchGameBtn.Background = btnBgBrush;
                LaunchGameBtn.Foreground = btnFgBrush;
            }

            if (SaveConfigBtn != null)
            {
                SaveConfigBtn.Background = btnBgBrush;
                SaveConfigBtn.Foreground = btnFgBrush;
            }

            if (SaveLaunchSettingsBtn != null)
            {
                SaveLaunchSettingsBtn.Background = btnBgBrush;
                SaveLaunchSettingsBtn.Foreground = btnFgBrush;
            }

            if (StickyHeaderBar != null)
            {
                StickyHeaderBar.Background = stickyBgBrush;
            }

            if (StickyHeaderTitle != null)
            {
                StickyHeaderTitle.Foreground = headerBrush;
            }

            if (ActiveConfigTitle != null)
            {
                ActiveConfigTitle.Foreground = headerBrush;
            }
        }

        private async void PaletteSelectorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PaletteSelectorCombo.SelectedItem is ColorPalette palette)
            {
                ApplyColorPalette(palette);
                if (_activeProfile != null)
                {
                    _activeProfile.LaunchSettings.ColorPalette = palette.Id;
                    try
                    {
                        await _profileService.UpdateProfileAsync(_activeProfile);
                    }
                    catch { }
                }
            }
        }

        #endregion

        #endregion

        #region Display & Launch Settings

        private void ResolutionPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResolutionPresetCombo == null || ScreenWidthBox == null || ScreenHeightBox == null) return;
            if (ResolutionPresetCombo.SelectedIndex < 0) return;

            switch (ResolutionPresetCombo.SelectedIndex)
            {
                case 0: // 2160 x 1920
                    ScreenWidthBox.Value = 2160;
                    ScreenHeightBox.Value = 1920;
                    break;
                case 1: // 1920 x 1080
                    ScreenWidthBox.Value = 1920;
                    ScreenHeightBox.Value = 1080;
                    break;
                case 2: // 1080 x 1920
                    ScreenWidthBox.Value = 1080;
                    ScreenHeightBox.Value = 1920;
                    break;
                case 3: // 2560 x 1440
                    ScreenWidthBox.Value = 2560;
                    ScreenHeightBox.Value = 1440;
                    break;
                case 4: // 3840 x 2160
                    ScreenWidthBox.Value = 3840;
                    ScreenHeightBox.Value = 2160;
                    break;
            }
        }

        private void ResolutionValue_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (ScreenWidthBox == null || ScreenHeightBox == null || ResolutionPresetCombo == null) return;
            UpdateResolutionPresetSelection((int)ScreenWidthBox.Value, (int)ScreenHeightBox.Value);
        }

        private void UpdateResolutionPresetSelection(int w, int h)
        {
            if (ResolutionPresetCombo == null) return;

            if (w == 2160 && h == 1920) ResolutionPresetCombo.SelectedIndex = 0;
            else if (w == 1920 && h == 1080) ResolutionPresetCombo.SelectedIndex = 1;
            else if (w == 1080 && h == 1920) ResolutionPresetCombo.SelectedIndex = 2;
            else if (w == 2560 && h == 1440) ResolutionPresetCombo.SelectedIndex = 3;
            else if (w == 3840 && h == 2160) ResolutionPresetCombo.SelectedIndex = 4;
            else ResolutionPresetCombo.SelectedIndex = 5; // Custom
        }

        private bool _isUpdatingLaunchSettingsUI = false;

        private async Task ApplyLaunchSettingsToScriptAsync()
        {
            if (_isUpdatingLaunchSettingsUI || _activeProfile == null) return;

            var s = _activeProfile.LaunchSettings;
            if (ScreenWidthBox != null && !double.IsNaN(ScreenWidthBox.Value)) s.ScreenWidth = (int)ScreenWidthBox.Value;
            if (ScreenHeightBox != null && !double.IsNaN(ScreenHeightBox.Value)) s.ScreenHeight = (int)ScreenHeightBox.Value;
            if (FullscreenSwitch != null) s.Fullscreen = FullscreenSwitch.IsOn;
            if (PopupWindowSwitch != null) s.PopupWindow = PopupWindowSwitch.IsOn;
            if (ConsoleModeCombo != null && ConsoleModeCombo.SelectedIndex >= 0) s.ConsoleMode = (ConsoleOutputMode)ConsoleModeCombo.SelectedIndex;

            // In-place launch script update
            if (!string.IsNullOrEmpty(s.BatFilePath) && File.Exists(s.BatFilePath))
            {
                try
                {
                    _launchService.UpdateLaunchScriptInPlace(s.BatFilePath, s);
                }
                catch { }
            }

            try
            {
                await _profileService.UpdateProfileAsync(_activeProfile);
            }
            catch { }
        }

        private async void SaveLaunchSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            await ApplyLaunchSettingsToScriptAsync();
            ShowNotification("Settings Saved", "Display and launch parameters updated successfully in profile and launch script.", InfoBarSeverity.Success);
        }

        #endregion

        #region Configuration Editor

        private void ToggleFileBrowserBtn_Click(object sender, RoutedEventArgs e)
        {
            SetFileBrowserVisibility(!_isFileBrowserVisible);
        }

        private void CollapseFileBrowserBtn_Click(object sender, RoutedEventArgs e)
        {
            SetFileBrowserVisibility(false);
        }

        private void SetFileBrowserVisibility(bool visible)
        {
            _isFileBrowserVisible = visible;
            if (ConfigFileBrowserColumn != null)
            {
                ConfigFileBrowserColumn.Width = visible ? new GridLength(260) : new GridLength(0);
            }
            if (ConfigFileBrowserPane != null)
            {
                ConfigFileBrowserPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async void ConfigFilesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConfigFilesListView == null || ConfigFilesListView.SelectedItem is not ConfigFileItem item || _activeProfile == null) return;

            try
            {
                ActiveConfigTitle.Text = item.FileName;
                ActiveConfigSubtext.Text = string.Empty;

                // Automatically collapse file browser to switch to full header & document display
                SetFileBrowserVisibility(false);

                // Raw editor collapsed by default
                if (RawEditorColumn != null)
                {
                    RawEditorColumn.Width = new GridLength(0);
                    RawEditorColumn.MinWidth = 0;
                }

                // Check for AquaMai.toml: remove rendering and prompt user to install MaiChartManager
                if (Path.GetFileName(item.FullPath).Equals("AquaMai.toml", StringComparison.OrdinalIgnoreCase))
                {
                    ConfigPropertiesContainer.Children.Clear();
                    _propertyControls.Clear();
                    _sectionViews.Clear();
                    if (TocListView != null) TocListView.ItemsSource = null;
                    SetTocVisibility(false);
                    if (StickyHeaderBar != null) StickyHeaderBar.Visibility = Visibility.Collapsed;

                    var banner = new Border
                    {
                        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(24),
                        Margin = new Thickness(0, 16, 0, 16),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    var sp = new StackPanel { Spacing = 12 };
                    sp.Children.Add(new TextBlock { Text = "AquaMai.toml is managed externally", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                    sp.Children.Add(new TextBlock
                    {
                        Text = "Rendering for AquaMai.toml is disabled because a dedicated application exists for it.\nPlease install MaiChartManager from the Microsoft Store to configure AquaMai mods.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                    });
                    var storeBtn = new Button
                    {
                        Content = "Open Microsoft Store",
                        Style = (Style)Application.Current.Resources["AccentButtonStyle"]
                    };
                    storeBtn.Click += (btnS, btnE) =>
                    {
                        try { Process.Start(new ProcessStartInfo("ms-windows-store://search/?query=MaiChartManager") { UseShellExecute = true }); } catch { }
                    };
                    sp.Children.Add(storeBtn);
                    banner.Child = sp;
                    ConfigPropertiesContainer.Children.Add(banner);

                    RawConfigTextBox.Text = "# AquaMai rendering is managed externally via MaiChartManager.\n# Install MaiChartManager from the Microsoft Store to configure AquaMai mods.";
                    return;
                }

                _currentConfigDoc = await _configService.LoadConfigAsync(item.FullPath, _activeProfile.GameDirectory);
                ActiveConfigSubtext.Text = string.Empty;
                RenderConfigProperties(_currentConfigDoc);

                // Populate raw textbox
                if (File.Exists(item.FullPath))
                {
                    RawConfigTextBox.Text = await File.ReadAllTextAsync(item.FullPath);
                }
            }
            catch (Exception ex)
            {
                ShowNotification("Error Loading Config", ex.Message, InfoBarSeverity.Error);
            }
        }

        private void ConfigSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            FilterRenderedProperties(sender.Text);
        }

        private void FilterRenderedProperties(string? queryText)
        {
            var query = queryText?.Trim();
            if (string.IsNullOrEmpty(query))
            {
                foreach (var sv in _sectionViews)
                {
                    sv.Container.Visibility = Visibility.Visible;
                    foreach (var p in sv.Properties)
                    {
                        p.Row.Visibility = Visibility.Visible;
                    }
                }
                if (TocListView?.ItemsSource is List<TocItem> allItems && allItems.Count > 0)
                {
                    SetTocVisibility(true);
                }
                else
                {
                    SetTocVisibility(false);
                }
                return;
            }

            int visibleSections = 0;
            foreach (var sv in _sectionViews)
            {
                bool sectionMatches = sv.Section.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                      (sv.Section.Comment != null && sv.Section.Comment.Contains(query, StringComparison.OrdinalIgnoreCase));

                int matchCount = 0;
                foreach (var p in sv.Properties)
                {
                    bool propMatches = sectionMatches ||
                                       p.Prop.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                       (p.Prop.Comment != null && p.Prop.Comment.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                                       p.Prop.RawValue.Contains(query, StringComparison.OrdinalIgnoreCase);

                    p.Row.Visibility = propMatches ? Visibility.Visible : Visibility.Collapsed;
                    if (propMatches) matchCount++;
                }

                sv.Container.Visibility = matchCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (matchCount > 0) visibleSections++;
            }

            if (visibleSections == 0)
            {
                SetTocVisibility(false);
                if (StickyHeaderBar != null) StickyHeaderBar.Visibility = Visibility.Collapsed;
            }
            else if (TocListView?.ItemsSource is List<TocItem> tItems && tItems.Count > 0)
            {
                SetTocVisibility(true);
            }
            else
            {
                SetTocVisibility(false);
            }
        }

        private static string CleanCommentForDisplay(string comment)
        {
            if (string.IsNullOrWhiteSpace(comment)) return string.Empty;
            var lines = comment.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var cleanedLines = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                while (trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                {
                    trimmed = trimmed.Substring(1).Trim();
                }
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    cleanedLines.Add(trimmed);
                }
            }
            return string.Join(Environment.NewLine, cleanedLines);
        }

        private void TocListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isProgrammaticTocChange) return;

            if (TocListView.SelectedItem is TocItem item && item.Container != null)
            {
                item.Container.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = true,
                    VerticalAlignmentRatio = 0.0
                });
            }
        }

        private void SetTocVisibility(bool isVisible)
        {
            if (ConfigTocBorder != null)
            {
                ConfigTocBorder.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
            if (ConfigTocSeparator != null)
            {
                ConfigTocSeparator.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
            if (ConfigTocColumn != null)
            {
                if (isVisible)
                {
                    ConfigTocColumn.Width = new GridLength(170);
                    ConfigTocColumn.MinWidth = 120;
                }
                else
                {
                    ConfigTocColumn.Width = new GridLength(0);
                    ConfigTocColumn.MinWidth = 0;
                }
            }
            if (ConfigTocSeparatorColumn != null)
            {
                ConfigTocSeparatorColumn.Width = isVisible ? GridLength.Auto : new GridLength(0);
            }
            if (!isVisible && StickyHeaderBar != null)
            {
                StickyHeaderBar.Visibility = Visibility.Collapsed;
            }
        }

        private void ConfigTocBorder_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (ConfigTocBorder != null && ConfigTocBorder.Visibility == Visibility.Visible && ConfigTocColumn != null)
            {
                ConfigTocColumn.Width = new GridLength(320);
            }
        }

        private void ConfigTocBorder_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (ConfigTocBorder != null && ConfigTocBorder.Visibility == Visibility.Visible && ConfigTocColumn != null)
            {
                ConfigTocColumn.Width = new GridLength(170);
            }
        }

        private void ToggleRawEditorBtn_Click(object sender, RoutedEventArgs e)
        {
            if (RawEditorColumn == null) return;
            if (RawEditorColumn.Width.Value > 0)
            {
                RawEditorColumn.Width = new GridLength(0);
                RawEditorColumn.MinWidth = 0;
            }
            else
            {
                RawEditorColumn.Width = new GridLength(1, GridUnitType.Star);
                RawEditorColumn.MinWidth = 280;
            }
        }

        private void ConfigScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            UpdateStickyHeadersAndToc();
        }

        private void UpdateStickyHeadersAndToc()
        {
            if (ConfigScrollViewer == null || _sectionViews.Count == 0) return;

            if (TocListView?.ItemsSource is not List<TocItem> list || list.Count == 0)
            {
                if (StickyHeaderBar != null)
                {
                    StickyHeaderBar.Visibility = Visibility.Collapsed;
                }
                return;
            }

            SectionViewItem? activeSection = null;

            foreach (var sv in _sectionViews)
            {
                if (sv.Container.Visibility == Visibility.Collapsed) continue;

                try
                {
                    var transform = sv.Container.TransformToVisual(ConfigScrollViewer);
                    var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                    double containerTop = pt.Y;
                    double containerHeight = sv.Container.ActualHeight;

                    // If container is within upper region of viewport, consider it active
                    if (containerTop <= 60 && (containerTop + containerHeight) > 20)
                    {
                        activeSection = sv;
                    }
                }
                catch
                {
                    // Ignore layout measurement transient states
                }
            }

            activeSection ??= _sectionViews.FirstOrDefault(s => s.Container.Visibility == Visibility.Visible);

            if (activeSection != null)
            {
                if (StickyHeaderBar != null)
                {
                    StickyHeaderBar.Visibility = Visibility.Visible;
                    StickyHeaderTitle.Text = string.IsNullOrEmpty(activeSection.Section.Name) || activeSection.Section.Name == "Root"
                        ? "[General]"
                        : $"[{activeSection.Section.Name}]";

                    StickyHeaderComment.Text = CleanCommentForDisplay(activeSection.Section.Comment ?? string.Empty);
                    StickyHeaderPropCount.Text = $"{activeSection.Properties.Count} properties";
                }

                if (TocListView != null && TocListView.ItemsSource is List<TocItem> tocItems)
                {
                    var targetIdx = tocItems.FindIndex(t => t.Section == activeSection.Section);
                    if (targetIdx >= 0 && TocListView.SelectedIndex != targetIdx)
                    {
                        _isProgrammaticTocChange = true;
                        TocListView.SelectedIndex = targetIdx;
                        _isProgrammaticTocChange = false;
                    }
                }
            }
        }

        private void RenderConfigProperties(ConfigDocument doc)
        {
            ConfigPropertiesContainer.Children.Clear();
            _propertyControls.Clear();
            _sectionViews.Clear();

            if (TocListView != null)
            {
                TocListView.ItemsSource = null;
            }

            if (ConfigSearchBox != null)
            {
                ConfigSearchBox.Text = string.Empty;
            }

            if (!doc.Sections.Any() || doc.Sections.All(s => !s.Properties.Any()))
            {
                var emptyNotice = new TextBlock
                {
                    Text = "No editable properties detected in this configuration.",
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                };
                ConfigPropertiesContainer.Children.Add(emptyNotice);
                SetTocVisibility(false);
                if (StickyHeaderBar != null) StickyHeaderBar.Visibility = Visibility.Collapsed;
                return;
            }

            bool isPlainText = Path.GetExtension(doc.FilePath).Equals(".txt", StringComparison.OrdinalIgnoreCase);
            bool hasMultipleSections = doc.Sections.Count(s => s.Properties.Any()) > 1;
            bool hasDistinctSectionName = doc.Sections.Any(s => s.Properties.Any() && !string.IsNullOrEmpty(s.Name) && !s.Name.Equals("Root", StringComparison.OrdinalIgnoreCase) && !s.Name.Equals("General", StringComparison.OrdinalIgnoreCase));
            bool shouldHaveToc = !isPlainText && (hasMultipleSections || hasDistinctSectionName);

            var tocList = new List<TocItem>();

            foreach (var section in doc.Sections)
            {
                if (!section.Properties.Any()) continue;

                var sectionContainer = new StackPanel
                {
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 4, 0, 16)
                };

                // Section header block (text-document style: [SectionName])
                var headerPanel = new StackPanel
                {
                    Spacing = 4,
                    Margin = new Thickness(0, 4, 0, 6),
                    Padding = new Thickness(4, 6, 4, 6),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                if (isPlainText)
                {
                    headerPanel.Visibility = Visibility.Collapsed;
                }

                var sectionNameDisplay = string.IsNullOrEmpty(section.Name) || section.Name == "Root"
                    ? "[General]"
                    : $"[{section.Name}]";

                var headerRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Left
                };

                var headerText = new TextBlock
                {
                    Text = sectionNameDisplay,
                    FontSize = 16,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                headerRow.Children.Add(headerText);

                // Create tooltip from comments that appear when hovered on headers (Placed at Top per FEEDBACK.md)
                string tooltipContent = !string.IsNullOrWhiteSpace(section.Comment)
                    ? CleanCommentForDisplay(section.Comment)
                    : $"Configuration section {sectionNameDisplay}";

                var headerToolTip = new ToolTip
                {
                    Content = new TextBlock
                    {
                        Text = tooltipContent,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 420,
                        FontSize = 12
                    },
                    Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Top
                };
                ToolTipService.SetToolTip(headerPanel, headerToolTip);
                ToolTipService.SetPlacement(headerPanel, Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Top);

                if (!string.IsNullOrWhiteSpace(section.Comment))
                {
                    var infoIcon = new FontIcon
                    {
                        Glyph = "\uE946",
                        FontSize = 13,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ToolTipService.SetToolTip(infoIcon, headerToolTip);
                    ToolTipService.SetPlacement(infoIcon, Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Top);
                    headerRow.Children.Add(infoIcon);
                }

                headerPanel.Children.Add(headerRow);

                // Subtle underline divider
                var divider = new Border
                {
                    BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 2, 0, 4)
                };
                headerPanel.Children.Add(divider);

                if (!string.IsNullOrEmpty(section.Comment))
                {
                    var commentBlock = new TextBlock
                    {
                        Text = CleanCommentForDisplay(section.Comment),
                        FontSize = 11,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 700,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    headerPanel.Children.Add(commentBlock);
                }

                sectionContainer.Children.Add(headerPanel);

                var sectionView = new SectionViewItem
                {
                    Section = section,
                    Container = sectionContainer,
                    HeaderPanel = headerPanel
                };

                var propertiesPanel = new StackPanel
                {
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 0, 0, 0)
                };

                foreach (var prop in section.Properties)
                {
                    var propCard = CreatePropertyRow(prop);
                    propertiesPanel.Children.Add(propCard);
                    sectionView.Properties.Add((prop, propCard));
                }

                sectionContainer.Children.Add(propertiesPanel);
                ConfigPropertiesContainer.Children.Add(sectionContainer);
                _sectionViews.Add(sectionView);

                if (shouldHaveToc)
                {
                    tocList.Add(new TocItem
                    {
                        DisplayName = sectionNameDisplay,
                        FullName = section.Name,
                        Section = section,
                        Container = sectionContainer
                    });
                }
            }

            if (tocList.Count > 0)
            {
                SetTocVisibility(true);
                if (TocListView != null)
                {
                    TocListView.ItemsSource = tocList;
                    _isProgrammaticTocChange = true;
                    TocListView.SelectedIndex = 0;
                    _isProgrammaticTocChange = false;
                }
                UpdateStickyHeadersAndToc();
            }
            else
            {
                SetTocVisibility(false);
                if (TocListView != null)
                {
                    TocListView.ItemsSource = null;
                }
                if (StickyHeaderBar != null)
                {
                    StickyHeaderBar.Visibility = Visibility.Collapsed;
                }
            }
        }

        private FrameworkElement CreatePropertyRow(ConfigProperty prop)
        {
            var rowPanel = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 720,
                Margin = new Thickness(0, 2, 0, 10)
            };

            // Key Name Label
            var keyText = new TextBlock
            {
                Text = prop.Key,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            rowPanel.Children.Add(keyText);

            // Comment / Description
            if (!string.IsNullOrEmpty(prop.Comment))
            {
                var commentText = new TextBlock
                {
                    Text = prop.Comment,
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 660,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 2)
                };
                rowPanel.Children.Add(commentText);
            }

            FrameworkElement valueControl;

            // Check if property has discrete options (from comments or schemas)
            if (prop.Options != null && prop.Options.Count > 0)
            {
                var optionsList = new List<string>(prop.Options);

                if (_activeProfile != null)
                {
                    var customOpts = GetCustomOptionsForProperty(_activeProfile, prop.Key);
                    foreach (var cOpt in customOpts)
                    {
                        if (!optionsList.Contains(cOpt, StringComparer.OrdinalIgnoreCase))
                        {
                            optionsList.Add(cOpt);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(prop.RawValue) && !optionsList.Contains(prop.RawValue, StringComparer.OrdinalIgnoreCase))
                {
                    optionsList.Insert(0, prop.RawValue);
                }

                var combo = new ComboBox
                {
                    Width = prop.AllowCustomOptions ? 280 : 240,
                    IsEditable = prop.AllowCustomOptions,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                combo.ItemsSource = optionsList;
                var selected = optionsList.FirstOrDefault(o => string.Equals(o, prop.RawValue, StringComparison.OrdinalIgnoreCase)) ?? optionsList.FirstOrDefault();
                combo.SelectedItem = selected;

                combo.SelectionChanged += (s, e) =>
                {
                    if (combo.SelectedItem != null)
                    {
                        prop.RawValue = combo.SelectedItem.ToString() ?? "";
                    }
                };

                if (prop.AllowCustomOptions)
                {
                    var optPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    optPanel.Children.Add(combo);

                    var addBtn = new Button
                    {
                        Content = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 6,
                            Children =
                            {
                                new FontIcon { Glyph = "\uE710", FontSize = 12 },
                                new TextBlock { Text = "Add Option", FontSize = 12 }
                            }
                        },
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    addBtn.Click += async (btnSender, btnArgs) =>
                    {
                        if (this.Content?.XamlRoot == null) return;
                        var dialog = new ContentDialog
                        {
                            Title = $"Add Option for {prop.Key}",
                            PrimaryButtonText = "Add",
                            CloseButtonText = "Cancel",
                            XamlRoot = this.Content.XamlRoot
                        };

                        var input = new TextBox
                        {
                            PlaceholderText = $"Enter new {prop.Key} option...",
                            Width = 320
                        };
                        dialog.Content = input;

                        dialog.PrimaryButtonClick += async (dSender, dArgs) =>
                        {
                            var newVal = input.Text.Trim();
                            if (!string.IsNullOrEmpty(newVal))
                            {
                                var current = (combo.ItemsSource as IEnumerable<string>)?.ToList() ?? new List<string>();
                                if (!current.Contains(newVal, StringComparer.OrdinalIgnoreCase))
                                {
                                    current.Add(newVal);
                                    combo.ItemsSource = current;
                                }
                                combo.SelectedItem = newVal;
                                prop.RawValue = newVal;
                                if (prop.Options != null && !prop.Options.Contains(newVal, StringComparer.OrdinalIgnoreCase))
                                {
                                    prop.Options.Add(newVal);
                                }

                                if (_activeProfile != null)
                                {
                                    SaveCustomOptionForProperty(_activeProfile, prop.Key, newVal);
                                    await _profileService.UpdateProfileAsync(_activeProfile);
                                }
                                ShowNotification("Option Added", $"Added '{newVal}' to {prop.Key} options.", InfoBarSeverity.Success);
                            }
                        };

                        await dialog.ShowAsync();
                    };

                    optPanel.Children.Add(addBtn);
                    valueControl = optPanel;
                }
                else
                {
                    valueControl = combo;
                }
            }
            else
            {
                switch (prop.ValueType)
                {
                    case PropertyValueType.Boolean:
                        var toggle = new ToggleSwitch
                        {
                            IsOn = prop.AsBoolean(),
                            OnContent = "Enabled",
                            OffContent = "Disabled",
                            HorizontalAlignment = HorizontalAlignment.Left
                        };
                        toggle.Toggled += (s, e) =>
                        {
                            prop.SetFromBoolean(toggle.IsOn, asNumeric: prop.RawValue == "1" || prop.RawValue == "0");
                        };
                        valueControl = toggle;
                        break;

                    case PropertyValueType.Integer:
                    case PropertyValueType.Float:
                        var numBox = new NumberBox
                        {
                            Value = double.TryParse(prop.RawValue, out var val) ? val : 0,
                            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                            Width = 160,
                            HorizontalAlignment = HorizontalAlignment.Left
                        };
                        numBox.ValueChanged += (s, e) =>
                        {
                            prop.RawValue = numBox.Value.ToString();
                        };
                        valueControl = numBox;
                        break;

                    case PropertyValueType.FilePath:
                    case PropertyValueType.DirectoryPath:
                        var pathGrid = new Grid
                        {
                            Width = 500,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            ColumnDefinitions =
                            {
                                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                                new ColumnDefinition { Width = GridLength.Auto },
                                new ColumnDefinition { Width = GridLength.Auto }
                            }
                        };

                        var pathInput = new TextBox
                        {
                            Text = prop.RawValue
                        };

                        var statusIcon = new FontIcon
                        {
                            Glyph = prop.IsValid ? "\uE73E" : "\uE783",
                            Foreground = prop.IsValid
                                ? new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 65))
                                : new SolidColorBrush(ColorHelper.FromArgb(255, 216, 59, 1)),
                            FontSize = 16,
                            Margin = new Thickness(8, 0, 8, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };

                        ToolTipService.SetToolTip(statusIcon, prop.ValidationErrorMessage ?? (prop.IsValid ? "Valid file path" : "Invalid path"));

                        pathInput.TextChanged += (s, e) =>
                        {
                            prop.RawValue = pathInput.Text;
                            if (_activeProfile != null)
                            {
                                _validationService.ValidateProperty(prop, _activeProfile.GameDirectory, out string? err);
                                statusIcon.Glyph = prop.IsValid ? "\uE73E" : "\uE783";
                                statusIcon.Foreground = prop.IsValid
                                    ? new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 65))
                                    : new SolidColorBrush(ColorHelper.FromArgb(255, 216, 59, 1));
                                ToolTipService.SetToolTip(statusIcon, err ?? "Valid file path");
                            }
                        };

                        var browseBtn = new Button
                        {
                            Content = "Browse...",
                            Margin = new Thickness(4, 0, 0, 0)
                        };

                        browseBtn.Click += async (s, e) =>
                        {
                            await ShowPathBrowseAsync(pathInput, prop);
                        };

                        Grid.SetColumn(pathInput, 0);
                        Grid.SetColumn(statusIcon, 1);
                        Grid.SetColumn(browseBtn, 2);

                        pathGrid.Children.Add(pathInput);
                        pathGrid.Children.Add(statusIcon);
                        pathGrid.Children.Add(browseBtn);
                        valueControl = pathGrid;
                        break;

                    case PropertyValueType.String:
                    default:
                        var textBox = new TextBox
                        {
                            Text = prop.RawValue,
                            Width = 400,
                            HorizontalAlignment = HorizontalAlignment.Left
                        };
                        textBox.TextChanged += (s, e) =>
                        {
                            prop.RawValue = textBox.Text;
                        };
                        valueControl = textBox;
                        break;
                }
            }

            _propertyControls[prop] = valueControl;
            rowPanel.Children.Add(valueControl);
            return rowPanel;
        }

        private async Task ShowPathBrowseAsync(TextBox targetBox, ConfigProperty prop)
        {
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (prop.ValueType == PropertyValueType.DirectoryPath)
                {
                    var picker = new FolderPicker();
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
                    picker.FileTypeFilter.Add("*");
                    var folder = await picker.PickSingleFolderAsync();
                    if (folder != null)
                    {
                        targetBox.Text = folder.Path;
                        return;
                    }
                }
                else
                {
                    var picker = new FileOpenPicker();
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
                    if (prop.AllowedExtensions.Any())
                    {
                        foreach (var ext in prop.AllowedExtensions) picker.FileTypeFilter.Add(ext);
                    }
                    else
                    {
                        picker.FileTypeFilter.Add("*");
                    }
                    var file = await picker.PickSingleFileAsync();
                    if (file != null)
                    {
                        targetBox.Text = file.Path;
                        return;
                    }
                }
            }
            catch { }

            // Fallback dialog if picker canceled or failed
            ShowPathFallbackDialog(targetBox, prop);
        }

        private void ShowPathFallbackDialog(TextBox targetBox, ConfigProperty prop)
        {
            if (this.Content?.XamlRoot == null) return;

            var dialog = new ContentDialog
            {
                Title = "Enter File / Directory Path",
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };

            var sp = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
            if (prop.AllowedExtensions.Any())
            {
                sp.Children.Add(new TextBlock
                {
                    Text = $"Allowed formats: {string.Join(", ", prop.AllowedExtensions)}",
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    FontSize = 12
                });
            }

            var input = new TextBox { Text = targetBox.Text, Width = 380 };
            sp.Children.Add(input);
            dialog.Content = sp;

            dialog.PrimaryButtonClick += (s, args) =>
            {
                targetBox.Text = input.Text;
            };

            _ = dialog.ShowAsync();
        }

        private static List<string> GetCustomOptionsForProperty(Profile profile, string key)
        {
            var lower = key.ToLowerInvariant();
            if (lower is "default" or "dns") return profile.LaunchSettings.CustomDnsOptions;
            if (lower is "aimedb") return profile.LaunchSettings.CustomAimeDbOptions;
            if (lower is "id" or "keychip") return profile.LaunchSettings.CustomKeychipOptions;
            return new List<string>();
        }

        private static void SaveCustomOptionForProperty(Profile profile, string key, string value)
        {
            var list = GetCustomOptionsForProperty(profile, key);
            if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(value);
            }
        }

        private async void SaveConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentConfigDoc == null) return;

            try
            {
                await _configService.SaveConfigAsync(_currentConfigDoc);
                ShowNotification("Configuration Saved", $"Changes saved to {System.IO.Path.GetFileName(_currentConfigDoc.FilePath)}.", InfoBarSeverity.Success);

                // Update raw box
                if (File.Exists(_currentConfigDoc.FilePath))
                {
                    RawConfigTextBox.Text = await File.ReadAllTextAsync(_currentConfigDoc.FilePath);
                }

                // Verify configuration correctness for launch readiness
                await CheckAndVerifyConfigStatusAsync();
            }
            catch (Exception ex)
            {
                ShowNotification("Save Failed", ex.Message, InfoBarSeverity.Error);
            }
        }

        private async void ReloadConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentConfigDoc == null || _activeProfile == null) return;
            try
            {
                _currentConfigDoc = await _configService.LoadConfigAsync(_currentConfigDoc.FilePath, _activeProfile.GameDirectory);
                RenderConfigProperties(_currentConfigDoc);
                RawConfigTextBox.Text = await File.ReadAllTextAsync(_currentConfigDoc.FilePath);
                ShowNotification("Configuration Reloaded", "Reloaded file from disk.", InfoBarSeverity.Informational);
            }
            catch (Exception ex)
            {
                ShowNotification("Reload Failed", ex.Message, InfoBarSeverity.Error);
            }
        }

        #endregion

        #region Profile Management & Startup Wizard

        private void ShowStartupOverlay(bool isEditing, Profile? profileToEdit = null)
        {
            _isEditingProfile = isEditing;
            _editingProfileId = profileToEdit?.Id;

            WizardTitle.Text = isEditing ? "Edit Player Profile" : "Create Player Profile";
            SubmitProfileBtn.Content = isEditing ? "Save Changes" : "Create Profile";
            CancelProfileBtn.Visibility = (_allProfiles != null && _allProfiles.Any()) ? Visibility.Visible : Visibility.Collapsed;

            if (isEditing && profileToEdit != null)
            {
                ProfileNameInput.Text = profileToEdit.Name;
                GameDirInput.Text = profileToEdit.GameDirectory;
                AvatarPathInput.Text = profileToEdit.ProfilePicturePath ?? string.Empty;
                SetGameRadio(profileToEdit.Game);
            }
            else
            {
                ProfileNameInput.Text = $"Player {(_allProfiles?.Count ?? 0) + 1}";
                AvatarPathInput.Text = string.Empty;
                GameRadioMaimai.IsChecked = true;
            }

            StartupOverlay.Visibility = Visibility.Visible;
            DashboardRoot.Visibility = Visibility.Collapsed;
        }

        private void SetGameRadio(GameType game)
        {
            if (game == GameType.Chunithm)
            {
                if (GameRadioChunithm != null) GameRadioChunithm.IsChecked = true;
            }
            else
            {
                if (GameRadioMaimai != null) GameRadioMaimai.IsChecked = true;
            }
        }

        private GameType GetSelectedGameRadio()
        {
            if (GameRadioChunithm != null && GameRadioChunithm.IsChecked == true)
            {
                return GameType.Chunithm;
            }
            return GameType.Maimai;
        }

        private void GameRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (GameDirInput == null || WizardScanInfoBar == null) return;

            if (ReferenceEquals(sender, GameRadioChunithm) && (string.IsNullOrWhiteSpace(GameDirInput.Text) || GameDirInput.Text.Contains("ga 1.65")))
            {
                const string defaultChuniDir = @"C:\Users\Baconway\Downloads\CHUNITHM Mate";
                if (Directory.Exists(defaultChuniDir))
                {
                    GameDirInput.Text = defaultChuniDir;
                }
            }
            else if (ReferenceEquals(sender, GameRadioMaimai) && (string.IsNullOrWhiteSpace(GameDirInput.Text) || GameDirInput.Text.Contains("CHUNITHM Mate")))
            {
                const string defaultGaDir = @"C:\Users\Baconway\Downloads\ga 1.65";
                if (Directory.Exists(defaultGaDir))
                {
                    GameDirInput.Text = defaultGaDir;
                }
            }

            if (!string.IsNullOrEmpty(GameDirInput.Text))
            {
                _ = ScanGameDirectoryAsync(GameDirInput.Text, GetSelectedGameRadio());
            }
        }

        private async void GameDirInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (GameDirInput == null || WizardScanInfoBar == null) return;
            var path = GameDirInput.Text.Trim();
            if (Directory.Exists(path))
            {
                await ScanGameDirectoryAsync(path, GetSelectedGameRadio());
            }
            else
            {
                WizardScanInfoBar.IsOpen = false;
            }
        }

        private async Task ScanGameDirectoryAsync(string path, GameType game)
        {
            if (WizardScanInfoBar == null) return;

            var result = await _scannerService.ScanGameFolderAsync(path, game);
            WizardScanInfoBar.IsOpen = true;
            WizardScanInfoBar.Severity = result.IsValid ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            WizardScanInfoBar.Title = result.IsValid ? "Game Detected" : "Warning";
            if (result.IsValid && result.DetectedConfigFiles.Any(f => Path.GetFileName(f).Equals("AquaMai.toml", StringComparison.OrdinalIgnoreCase)))
            {
                WizardScanInfoBar.Message = $"{result.StatusMessage} AquaMai.toml detected (managed externally via MaiChartManager).";
            }
            else
            {
                WizardScanInfoBar.Message = result.StatusMessage;
            }
        }

        private void AvatarPathInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (AvatarPathInput == null || AvatarPreview == null) return;
            var path = AvatarPathInput.Text.Trim();
            if (File.Exists(path))
            {
                try
                {
                    AvatarPreview.ProfilePicture = new BitmapImage(new Uri(path));
                }
                catch
                {
                    AvatarPreview.ProfilePicture = null;
                }
            }
            else
            {
                AvatarPreview.ProfilePicture = null;
            }
        }

        private async void BrowseAvatarBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    AvatarPathInput.Text = file.Path;
                    return;
                }
            }
            catch { }

            // Fallback prompt dialog
            if (this.Content?.XamlRoot == null) return;
            var dialog = new ContentDialog
            {
                Title = "Enter Avatar Image Path",
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };

            var input = new TextBox { PlaceholderText = @"e.g. C:\Users\...\avatar.png", Width = 380 };
            dialog.Content = input;
            dialog.PrimaryButtonClick += (s, args) =>
            {
                AvatarPathInput.Text = input.Text;
            };
            _ = dialog.ShowAsync();
        }

        private async void BrowseGameDirBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FolderPicker();
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
                picker.FileTypeFilter.Add("*");
                var folder = await picker.PickSingleFolderAsync();
                if (folder != null)
                {
                    GameDirInput.Text = folder.Path;
                    return;
                }
            }
            catch { }

            // Fallback prompt dialog
            if (this.Content?.XamlRoot == null) return;
            var dialog = new ContentDialog
            {
                Title = "Enter Game Directory Path",
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };

            var input = new TextBox { Text = GameDirInput.Text, Width = 380 };
            dialog.Content = input;
            dialog.PrimaryButtonClick += (s, args) =>
            {
                GameDirInput.Text = input.Text;
            };
            _ = dialog.ShowAsync();
        }

        private async void SubmitProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            var name = ProfileNameInput.Text.Trim();
            var dir = GameDirInput.Text.Trim();
            var game = GetSelectedGameRadio();
            var avatar = AvatarPathInput.Text.Trim();

            if (string.IsNullOrEmpty(name))
            {
                WizardScanInfoBar.IsOpen = true;
                WizardScanInfoBar.Severity = InfoBarSeverity.Error;
                WizardScanInfoBar.Title = "Missing Name";
                WizardScanInfoBar.Message = "Please enter a profile name.";
                return;
            }

            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                WizardScanInfoBar.IsOpen = true;
                WizardScanInfoBar.Severity = InfoBarSeverity.Error;
                WizardScanInfoBar.Title = "Invalid Directory";
                WizardScanInfoBar.Message = "Please provide an existing game folder directory.";
                return;
            }

            // Enforce required files per FEEDBACK.md: segatools.ini and mai2.ini must be present
            var scanCheck = await _scannerService.ScanGameFolderAsync(dir, game);
            if (!scanCheck.IsValid)
            {
                WizardScanInfoBar.IsOpen = true;
                WizardScanInfoBar.Severity = InfoBarSeverity.Error;
                WizardScanInfoBar.Title = "Missing Required Configuration Files";
                WizardScanInfoBar.Message = scanCheck.StatusMessage;
                return;
            }

            // Check if AquaMai.toml is detected in the game folder
            bool hasAquaMai = scanCheck.DetectedConfigFiles.Any(f => Path.GetFileName(f).Equals("AquaMai.toml", StringComparison.OrdinalIgnoreCase))
                || File.Exists(Path.Combine(dir, "AquaMai.toml"))
                || File.Exists(Path.Combine(dir, "Package", "AquaMai.toml"));

            bool shouldShowStoreReminder = false;
            if (hasAquaMai)
            {
                if (_isEditingProfile && !string.IsNullOrEmpty(_editingProfileId))
                {
                    var existingProfile = await _profileService.GetProfileByIdAsync(_editingProfileId);
                    if (existingProfile != null && (!string.Equals(existingProfile.GameDirectory, dir, StringComparison.OrdinalIgnoreCase) || !existingProfile.StoreReminderShown))
                    {
                        shouldShowStoreReminder = true;
                    }
                }
                else
                {
                    shouldShowStoreReminder = true;
                }
            }

            if (shouldShowStoreReminder && this.Content?.XamlRoot != null)
            {
                var dialog = new ContentDialog
                {
                    Title = "MaiChartManager Recommended",
                    Content = "AquaMai.toml was detected in this game folder.\n\nConfiguration rendering for AquaMai.toml is managed externally because a dedicated application exists for it.\n\nPlease install MaiChartManager from the Microsoft Store.",
                    PrimaryButtonText = "Open Microsoft Store",
                    CloseButtonText = "Close",
                    XamlRoot = this.Content.XamlRoot
                };
                dialog.PrimaryButtonClick += (dSender, dArgs) =>
                {
                    try { Process.Start(new ProcessStartInfo("ms-windows-store://search/?query=MaiChartManager") { UseShellExecute = true }); } catch { }
                };
                await dialog.ShowAsync();
            }

            if (_isEditingProfile && !string.IsNullOrEmpty(_editingProfileId))
            {
                var existing = await _profileService.GetProfileByIdAsync(_editingProfileId);
                if (existing != null)
                {
                    existing.Name = name;
                    existing.Game = game;
                    existing.GameDirectory = dir;
                    existing.ProfilePicturePath = string.IsNullOrEmpty(avatar) ? null : avatar;
                    if (hasAquaMai)
                    {
                        existing.StoreReminderShown = true;
                    }
                    await _profileService.UpdateProfileAsync(existing);
                    _activeProfile = existing;
                }
            }
            else
            {
                var newProfile = new Profile
                {
                    Name = name,
                    Game = game,
                    GameDirectory = dir,
                    ProfilePicturePath = string.IsNullOrEmpty(avatar) ? null : avatar,
                    StoreReminderShown = true,
                    LaunchSettings = new LaunchSettings
                    {
                        ScreenWidth = game == GameType.Maimai ? 2160 : 1920,
                        ScreenHeight = game == GameType.Maimai ? 1920 : 1080,
                        Fullscreen = false,
                        PopupWindow = true
                    }
                };

                _activeProfile = await _profileService.CreateProfileAsync(newProfile);
                await _profileService.SetActiveProfileAsync(_activeProfile.Id);
            }

            _allProfiles = await _profileService.LoadProfilesAsync();
            StartupOverlay.Visibility = Visibility.Collapsed;
            DashboardRoot.Visibility = Visibility.Visible;

            RefreshProfileSelector();
            await LoadActiveProfileUIAsync();
            ShowNotification("Profile Ready", $"Switched to active profile: {_activeProfile?.Name}.", InfoBarSeverity.Success);
        }

        private void CancelProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_allProfiles.Any())
            {
                StartupOverlay.Visibility = Visibility.Collapsed;
                DashboardRoot.Visibility = Visibility.Visible;
            }
        }

        private void NewProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowStartupOverlay(isEditing: false);
        }

        private void EditProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_activeProfile != null)
            {
                ShowStartupOverlay(isEditing: true, profileToEdit: _activeProfile);
            }
        }

        private async void DeleteProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_activeProfile == null) return;

            if (this.Content?.XamlRoot == null) return;

            var dialog = new ContentDialog
            {
                Title = "Delete Profile",
                Content = $"Are you sure you want to delete profile '{_activeProfile.Name}'?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await _profileService.DeleteProfileAsync(_activeProfile.Id);
                _allProfiles = await _profileService.LoadProfilesAsync();

                if (!_allProfiles.Any())
                {
                    ShowStartupOverlay(isEditing: false);
                }
                else
                {
                    _activeProfile = await _profileService.GetActiveProfileAsync();
                    RefreshProfileSelector();
                    await LoadActiveProfileUIAsync();
                }

                ShowNotification("Profile Deleted", "Profile was successfully removed.", InfoBarSeverity.Informational);
            }
        }

        private async void ProfileSelectorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProfileSelectorCombo == null || _allProfiles == null) return;
            if (ProfileSelectorCombo.SelectedIndex < 0 || ProfileSelectorCombo.SelectedIndex >= _allProfiles.Count) return;

            var selected = _allProfiles[ProfileSelectorCombo.SelectedIndex];
            if (_activeProfile?.Id != selected.Id)
            {
                _activeProfile = selected;
                await _profileService.SetActiveProfileAsync(selected.Id);
                await LoadActiveProfileUIAsync();
                ShowNotification("Profile Switched", $"Active profile set to {selected.Name}.", InfoBarSeverity.Informational);
            }
        }

        #endregion

        private void ShowNotification(string title, string message, InfoBarSeverity severity)
        {
            if (MainInfoBar == null) return;
            MainInfoBar.Title = title;
            MainInfoBar.Message = message;
            MainInfoBar.Severity = severity;
            MainInfoBar.IsOpen = true;
        }
    }
}
