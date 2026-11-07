# AGENT.md - Developer & Agent Guide for sinmai-launcher

Welcome to **sinmai-launcher**! This document explains the architecture, design principles, module layout, and implementation details so future agents and developers can maintain and extend the codebase.

---

## 1. Project Overview

`sinmai-launcher` is a modern Windows desktop launcher and configuration editor for arcade rhythm games:
- **maimai DX** (Unity-based, tested with `C:\Users\Baconway\Downloads\ga 1.65`)
- **CHUNITHM** (tested with `C:\Users\Baconway\Downloads\CHUNITHM Mate`, supports `chusanApp.exe` and `chuniApp.exe`)
- **O.N.G.E.K.I.** (Unity-based)

The app is built on **WinUI 3 (Windows App SDK)** running unpackaged with native Windows 11 Fluent Design (`MicaBackdrop`).

---

## 2. Solution Architecture

The solution uses a clean three-project architecture separating UI, domain logic, and unit tests:

```text
sinmai-launcher/
├── sinmai-launcher.slnx                # Solution definition
├── sinmai-launcher.csproj              # WinUI 3 Desktop App (unpackaged)
│   ├── App.xaml / App.xaml.cs          # Application entry and unhandled exception logging
│   └── MainWindow.xaml / .cs           # Dashboard, tabs, dynamic config UI, profile wizard
│
├── sinmai-launcher.Core/               # Domain Models & Business Logic Library (.NET 8)
│   ├── Models/
│   │   ├── GameType.cs                 # Supported game types (Maimai, Ongeki, Chunithm)
│   │   ├── Profile.cs                  # Player profile entity
│   │   ├── LaunchSettings.cs           # Screen resolution, fullscreen, console modes
│   │   ├── ConfigProperty.cs           # Typed property model (bool, int, float, path, string)
│   │   ├── ConfigSection.cs            # Section grouping and comments
│   │   ├── ConfigDocument.cs           # Full config document representation
│   │   └── GameScanResult.cs           # Result of directory discovery
│   │
│   └── Services/
│       ├── IProfileService.cs / ProfileService.cs        # JSON CRUD in %LOCALAPPDATA%
│       ├── IGameScannerService.cs / GameScannerService.cs# File & mod detection
│       ├── IValidationService.cs / ValidationService.cs  # Strict path & type validation
│       ├── IConfigService.cs / ConfigService.cs          # Multi-format config coordinator
│       ├── ILaunchService.cs / LaunchService.cs          # Script modifier & process runner
│       └── Parsers/
│           ├── IniConfigParser.cs      # INI and CONF/CFG parser & serializer
│           ├── TomlConfigParser.cs     # TOML parser (Tomlyn) with AquaMai schema rules
│           └── YamlConfigParser.cs     # YAML parser (YamlDotNet)
│
└── sinmai-launcher.Tests/              # xUnit Automated Test Suite (.NET 8)
    ├── ProfileServiceTests.cs          # Profile JSON persistence & switching
    ├── GameScannerServiceTests.cs      # Game folder scanning & mod detection
    ├── ConfigParserTests.cs            # INI, TOML, YAML, CONF round-trip fidelity
    ├── ValidationServiceTests.cs       # Strict extension & path existence checks
    └── LaunchServiceTests.cs           # Injection protection & screen arg rewriting
```

---

## 3. Core Subsystems

### 3.1. Profile Management, Color Palettes & Dashboard Header (`ProfileService`)
- Profiles are saved as JSON at:
  `%LOCALAPPDATA%\sinmai-launcher\profiles.json`
- An active profile marker is saved at:
  `%LOCALAPPDATA%\sinmai-launcher\active_profile.txt`
- Each profile supports:
  - Custom display name and avatar picture (`.png`, `.jpg`).
  - Target `GameType` (supports **maimai DX** and **CHUNITHM** with interactive radio selection).
  - Target game directory (maimai DX requires `segatools.ini` and `mai2.ini`; CHUNITHM requires `segatools.ini`).
  - Per-profile display & launch settings (default 2160x1920 for maimai DX, 1920x1080 for CHUNITHM).
  - Selected `ColorPalette` theme.
- **Profile Creation & Cancellation**:
  - Profile creation supports **maimai DX** and **CHUNITHM**.
  - When initiating "New Profile" creation or editing an existing profile, if one or more profiles already exist (`_allProfiles.Any()`), a prominent "Cancel" button (`CancelProfileBtn`) is displayed in the startup overlay.
  - Clicking "Cancel" cleanly aborts creation/editing and returns the user to the active dashboard without mutating the profile list or current active profile.
  - When no profiles exist (first-time startup), the Cancel button is suppressed to guide the user through initial onboarding.
  - **Directory Requirement Check**: For maimai DX, `segatools.ini` and `mai2.ini` must be present. For CHUNITHM, `segatools.ini` must be present (auto-initialized from base file if missing). Profile creation is blocked with an informative error banner until valid files are present.
- **Top Header Bar Architecture**:
  - Left: Active player avatar, name, and game badge (`maimai DX` or `CHUNITHM`). Filepaths are deliberately hidden from all primary views to keep the interface uncluttered, confined strictly to the 'Game & Mods' info section.
  - Center: **`Launch Game`** button, `Stop Game` button, progress ring, and real-time status badge (`Ready to Launch`).
  - Right: Profile selector, CRUD buttons, and the **Custom Color Palette Selector**.
- **Custom Color Palettes Engine (`ColorPalette`)**:
  - Supports custom palette presets:
    - **Maimai DX Blue**: Deep slate/navy backdrop with bright sky blue accents.
    - **Raspberry DX**: Velvet plum backdrop with vibrant arcade pink/magenta accents.
    - **Ongeki Sunset**: Warm espresso backdrop with luminous electric amber accents.
    - **Cyber Neon**: Obsidian violet backdrop with laser purple accents.
    - **Emerald Mint**: Forest jade backdrop with radiant arcade mint accents.
    *(Note: `Clean Light` has been removed).*
  - Dynamic palette switching dynamically paints window backgrounds, card surfaces, headers, and action buttons in real-time, saving the choice directly into the user's active profile.
- **Launch Readiness & Configuration Verification**:
  - "Ready to Launch" does not merely check batch file existence; it actively invokes `_configService.ValidateGameConfigurationsAsync` across all scanned configuration files.
  - If syntax or type errors occur, status updates to "Config Check Warning" with full validation diagnostics in the tooltip.

### 3.2. Game Scanner & Strict File Discovery (`GameScannerService`)
- Scans target game directory and `Package/` subdirectories.
- Detects executables: `Sinmai.exe` (maimai DX), `chusanApp.exe` and `chuniApp.exe` (CHUNITHM), `ongeki.exe` (O.N.G.E.K.I.).
- Detects launch batch scripts: searches the parent directory of the detected executable first (`Package\launch.bat` or `game.bat`), falling back to the game root directory.
- **Strict File Filtering for maimai DX**:
  - Requires `segatools.ini` and `mai2.ini` to be present. If either file is absent, directory discovery fails and user cannot proceed.
  - Scans exclusively the specified configuration files:
    - `AquaMai.toml`
    - `mai2.ini`
    - `segatools.ini`
    - `Sinmai-Assist\Config.yaml` (or `.yml`)
    - `Sinmai-Assist\KeyBindConfig.yaml` (or `.yml`)
    - `DEVICE\aime.txt`
  - Extraneous configuration files (`firewall.cfg`, `pxGetHwInfo.ini`, `maimai-rpc.ini`, `MuMod.toml`, `config_*.json`) are intentionally omitted from configuration lists to maintain a focused, clean environment.
- **CHUNITHM File Discovery**:
  - Requires `segatools.ini` and game executable (`chusanApp.exe`/`chuniApp.exe`) or batch script (`game.bat`/`launch.bat`).
  - Scans exclusively `segatools.ini` and `DEVICE\aime.txt` per specification (strictly no duplicate `segatools.ini` or extraneous config files rendered).
  - Auto-initializes `segatools.ini` from downloads base file and `DEVICE\aime.txt` if not yet present in the game directory.
- **Automatic `aime.txt` Initialization**:
  - If `./DEVICE/aime.txt` does not exist, the scanner automatically initializes the `DEVICE` directory and creates a blank `aime.txt` file so the user can immediately enter or generate card access codes.
- Identifies active mod libraries in `Package\Mods\*.dll`.

### 3.3. Configuration Engine & Type-Safe UI (`ConfigService`)
- Supports `.ini`, `.toml`, `.yaml`, and `.txt` (`aime.txt`).
- **External AquaMai Management via MaiChartManager**:
  - Direct rendering for `AquaMai.toml` is removed because a dedicated external application manages it.
  - When adding a game folder containing `AquaMai.toml` to a profile, a Microsoft Store recommendation dialog pops up once directing the player to install **`MaiChartManager`** (`ms-windows-store://search/?query=MaiChartManager`), persisting `StoreReminderShown = true` on the profile.
  - In the Configuration Editor, selecting `AquaMai.toml` cleanly displays an informational banner with an **`Open Microsoft Store`** button without repeatedly interrupting the player with modal popups.
- **`DEVICE\aime.txt` Structured & Raw Rendering**:
  - Automatically loads and parses `aime.txt` into an `[Aime Card]` section featuring an `AccessCode` string property.
  - Players can view and edit the 20-digit access code in the structured editor or the raw editor, saving changes directly back to disk.
- **Segatools.ini Base Comments & Extensible Dropdowns**:
  - Inherits complete tooltip comments from the base unmodified `segatools.ini` file (`C:\Users\Baconway\Downloads\segatools.ini`) or built-in schema fallback.
  - **Optional `aimeDB` Property**: The `aimeDB` property is marked optional (`IsOptional = true`). If set to `(Disabled)` or left empty, it safely comments out the line (`;AimeDB=`) without reporting validation errors.
  - **Extensible Dropdowns (`DNS`, `aimeDB`, `keychip`)**:
    - The `DNS` (`default`), `aimeDB`, and `keychip` (`id`) properties are rendered as interactive dropdowns.
    - Each includes an **`[ + Add Option ]`** button with a pop-up dialog that allows players to enter custom server URLs, endpoints, or keychip serials. Newly added options are immediately selected and persisted into the active player profile.
- **Three-Pane Workspace Layout**:
  - **Table of Contents (Left of Renderer)**: Displays a dedicated vertical navigation sidebar listing all document sections (`[General]`, `[Graphics]`, `[Sound]`, etc.).
    - **Hover Expansion**: On pointer hover, the TOC pane expands from 170px to 320px with unclipped, wrapped section titles so long names are completely visible.
    - Clicking any TOC entry smoothly scrolls the renderer to that section via `StartBringIntoView()`.
  - **Structured Property Renderer (Middle)**:
    - **Header Toolbar**: Displays the active configuration file name (`ActiveConfigTitle`) clearly without distracting format subtext (`ActiveConfigSubtext` collapsed).
    - **Dedicated Opaque Sticky Header Bar**: Positioned cleanly between the toolbar (filename) and the rendered config. Features a 100% solid, non-transparent background matching the active palette. As the user scrolls, it dynamically reflects the section currently in view.
    - **Top-Anchored Tooltips**: Tooltips generated from config comments are anchored with `PlacementMode.Top` above headers so property controls below are never obscured.
  - **Raw File Editor (Right of Renderer)**: Placed side-by-side on the right of the structured renderer. Collapsed by default (`Width="0"`) to maximize screen space; toggled open/closed via the toolbar **`[ 📄 Raw Editor ]`** button. When opened, spans the entire content box from top to bottom.
- **Filepaths Concealed**: Raw directory paths are hidden from the file list and editor headers, preserving privacy and visual cleanliness.
- **Collapsible File Browser**: The configuration file list sidebar automatically collapses upon selecting a file to give the editor full width, and can be toggled open anytime via the `[ 📁 Files ]` button.
- **Real-Time Settings Search**: Built-in search box filters visible sections and properties by key, comment, value, or section name in real-time.
- **Backup on Save**: Before overwriting a configuration file, a `.bak` copy is created automatically.

### 3.4. Safe In-Place Launch Script Engine (`LaunchService`)
- Targets the launch script located in the game executable's parent folder (`Package\launch.bat`).
- **In-Place Modification**: Edits the current script directly in-place without generating auxiliary scripts (`sinmai_launch.bat`).
- **Real-Time Application**: Modifying screen resolution (`ScreenWidthBox`, `ScreenHeightBox`), fullscreen mode (`FullscreenSwitch`), window style (`PopupWindowSwitch`), or console mode (`ConsoleModeCombo`) immediately applies the changes directly to `Package\launch.bat` in-place and updates the profile JSON.
- **Automatic Original Backup**: Creates a one-time backup (`launch.bat.orig`) before applying any modifications.
- **Injection Protection (Immutable)**:
  - AMDaemon execution (`start /MIN %DAEMON_EXE% -f -c ...` or `start "AM Daemon" /min inject ...`) is strictly preserved.
  - Segatools hooks (`mai2hook.dll`, `inject -d -k`), updateiofirm calls, and registry configurations are never modified or disabled.
- **Configurable Settings**:
  - Unity screen parameters: `-screen-fullscreen <0|1>`, `-screen-width <W>`, `-screen-height <H>`, and `-popupwindow`.
  - AMDaemon console window mode: Minimized (`/min`), Visible, or Hidden (`/B`).
- Launches the batch script directly in its containing directory with process tree tracking and a "Stop Game" action button.

---

## 4. How to Extend the Application

### 4.1. Adding Full Chunithm Support
1. When Chunithm game files are available, inspect the directory structure for `chuniApp.exe` and `segatools.ini`.
2. Add any Chunithm-specific configuration files to `GameScannerService.knownConfigNames`.
3. If Chunithm uses custom batch arguments, update `LaunchService.GenerateLaunchScript` to handle Chunithm executable signatures.

### 4.2. Adding New Configuration Formats or Mod Schemas
1. Create a parser in `sinmai-launcher.Core/Services/Parsers/` implementing load and serialize methods.
2. In `ConfigService.cs`, add the new file extension to `DetectFormat`.
3. To enforce custom file extensions for a new mod property, add schema rules in `TomlConfigParser.ApplySchemaRules` or `IniConfigParser.InferValueType`.

---

## 5. Verification & Testing

### Running Unit Tests
```powershell
dotnet test sinmai-launcher.Tests/sinmai-launcher.Tests.csproj
```

### Running Debug Build
```powershell
dotnet build sinmai-launcher.csproj -p:Platform=x64
Start-Process "bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\sinmai-launcher.exe"
```

### Building Release Build (x64)
```powershell
dotnet build sinmai-launcher.csproj -c Release -p:Platform=x64
```
Output executable:
`bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\sinmai-launcher.exe`

### Building the Distribution Installer
The repository includes an automated build and packaging script using Inno Setup 6:
- Script: `build-installer.ps1`
- Inno Setup Configuration: `installer/setup.iss`
- Output installer: `installer_output/sinmai-launcher-Setup.exe`

To generate the distribution installer in a single command:
```powershell
.\build-installer.ps1
```

Or manually:
```powershell
dotnet publish sinmai-launcher.csproj -c Release -p:Platform=x64 -p:PublishTrimmed=false -o dist\win-x64
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\setup.iss
```

Key features of the installer:
- Modern Inno Setup wizard with embedded application icon (`app.ico`).
- Complete self-contained unpackaged WinUI 3 + Windows App SDK payload.
- Dual install support: Current User (`%LOCALAPPDATA%\Programs\sinmai-launcher`) without elevation, or All Users (`C:\Program Files\sinmai-launcher`) with elevation.
- Optional Desktop shortcut creation and Start Menu integration.
- Full uninstallation support via Windows Settings / Add/Remove Programs.

---

## 6. Rendering & Lifecycle Notes (WinUI 3 Unpackaged)

### 6.1. Root Causes Solved for Non-Rendering Bug
1. **Premature XAML Value Assignment & Event Binding**:
   - In `MainWindow.xaml`, `NumberBox` had `Value="2160"` with `ValueChanged="ResolutionValue_Changed"`. During XAML parsing of `ScreenWidthBox`, `ResolutionValue_Changed` fired before `ScreenHeightBox` was constructed, triggering a `NullReferenceException` which wrapped into `XamlParseException: Failed to assign to property 'Microsoft.UI.Xaml.Controls.NumberBox.Value' [Line: 116 Position: 132]`.
   - **Fix**: Removed all inline event handlers (`ValueChanged`, `SelectionChanged`, `Checked`, `TextChanged`) from XAML markup. All event handlers are now wired in `root.Loaded` after the visual tree is fully instantiated, with null guards.
2. **Overlay Clipping**:
   - `StartupOverlay`'s `ScrollViewer` previously clipped content taller than the default window size.
   - **Fix**: Added explicit scrollbar visibility (`VerticalScrollBarVisibility="Auto"`), centered the card with margins, and set a default window size of 1150x800 centered on the primary display.
3. **TabView Default Selection**:
   - Added `SelectedIndex="0"` to ensure the first tab is selected immediately on startup.
4. **Session / Desktop Visibility**:
   - Automated test runners or background commands running in isolated desktop sessions must spawn processes on `WinSta0\Default` to render on the physical monitor. Live rendering verification was confirmed with screen capture showing the Mica backdrop, profile wizard, hero card, configuration editor, and mod explorer.

### 6.2. Configuration Table of Contents (SECTIONS) Behavior
- **Dynamic Visibility**:
  - The left sidebar in the Configuration Editor hosts the `SECTIONS` jump-list (`ConfigTocBorder`), vertical separator (`ConfigTocSeparator`), and grid columns (`ConfigTocColumn`, `ConfigTocSeparatorColumn`).
  - Controlled via `SetTocVisibility(bool isVisible)`.
  - When viewing plain text files (`aime.txt`), files whose rendering is redirected (`AquaMai.toml`), empty configuration files, or search queries with 0 matching sections, `SetTocVisibility(false)` collapses the sidebar (`Width = 0`), separator, and sticky header so the property editor occupies the full available width without leaving an empty column.
  - Multi-section configuration files (`segatools.ini`, `mai2.ini`, `Sinmai-Assist` configs) dynamically restore the sidebar (`Width = 170`, `MinWidth = 120`).
