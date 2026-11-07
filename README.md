# Masseter Drop

> **Your downloads. One click away.**  
> *Version 1.0 — Native Windows Desktop Downloads Utility*

![Masseter Drop](Assets/Icons/app.png)

---

## 1. Overview

**Masseter Drop** is a lightweight, high-performance Windows desktop utility inspired by the macOS Dock Downloads Stack, reimagined with a native Windows 11/10 Fluent-inspired dark design.

Windows users frequently interrupt their workflow to open File Explorer, navigate to the `Downloads` directory, find a recently downloaded item, and drag or open it. **Masseter Drop eliminates this friction.**

Running quietly in the background and pinned directly to the Windows taskbar or system tray, Masseter Drop automatically detects newly downloaded files in real-time, displays metadata, provides rich file-type icons, supports operating-system-level drag-and-drop, and offers instant one-click access.

---

## 2. Key Features

- **Real-Time Download Detection Engine**:
  - Leverages Windows Shell Known Folders API (`FOLDERID_Downloads`) — never hardcodes directory paths.
  - Monitors file creations, modifications, renames, and deletions using `FileSystemWatcher`.
  - Intelligently handles browser temporary files (`.crdownload`, `.part`, `.tmp`, `.download`, `.opdownload`).
  - Employs debouncing and write-lock verification (`FileShare.ReadWrite` lock polling with bounded backoff) to ensure files are only marked **Completed** after writing finishes.
- **True OS-Level Drag-and-Drop**:
  - Native Windows OLE/Shell drag-and-drop support (`DataFormats.FileDrop`).
  - Drag completed downloads directly into File Explorer, desktop folders, email composition windows (Outlook, webmail), messaging apps (Slack, Discord, Teams), or code editors.
- **Modern Minimal UI (Fluent Design)**:
  - Dark theme aesthetic by default (`#17191E` canvas, `#22252C` panels, `#70A5FF` accent).
  - High-resolution vector icon badges and native Windows Shell icon thumbnails.
  - Categorized file types: PDF, Images, Videos, Audio, ZIP archives, Documents, Executables, Code, and Other.
  - Windows 11 rounded corners via DWM API (`DwmSetWindowAttribute`) and subtle drop shadows.
- **Taskbar & Tray Integration**:
  - Pinned taskbar shortcut support with custom `AppUserModelID` (`Masseter.Drop.App.v1`).
  - Single-instance Named Pipe IPC (`MasseterDrop_IPC_SingleInstance`): Clicking the taskbar icon activates or toggles the existing instance rather than opening multiple processes.
  - System Tray notification icon with right-click menu and single/double-click panel toggle.
  - Intelligent screen edge and taskbar position calculation (`SHAppBarMessage`) ensuring the panel never clips off-screen.
- **Instant File Actions**:
  - Single-click selection.
  - Double-click to open in default Windows handler.
  - Context menu: Open, Show in File Explorer (`explorer.exe /select`), Copy File Path, Copy File (Clipboard), Remove from Recent List.
- **Live Search**:
  - Instant real-time filtering by filename and extension.
- **History & Persistence**:
  - Lightweight JSON metadata storage in `%APPDATA%\MasseterDrop\history.json`.
  - Automatic file availability checks on startup.
- **Application Preferences**:
  - Configurable monitored folder (default Downloads or custom).
  - Launch at Windows startup (Registry `HKCU\...\Run`).
  - Start minimized in background.
  - Max recent history limit (50, 100, 200, 500).
  - Theme selection (Dark / Light / System).

---

## 3. Architecture & Project Structure

The project follows clean Model-View-ViewModel (MVVM) separation without heavy third-party dependencies:

```
MasseterDrop/
│
├── Models/
│   ├── DownloadItem.cs           # File metadata model with change notifications and categorization
│   └── AppSettings.cs            # Persisted application configuration model
│
├── Services/
│   ├── DownloadMonitor.cs        # FileSystemWatcher engine, debouncing & completion lock detection
│   ├── DownloadHistoryService.cs # JSON history persistence & initial directory scanning
│   ├── FileOperationService.cs   # Shell execution, Explorer reveal, and clipboard operations
│   ├── SettingsService.cs        # Settings persistence and Windows Startup registry management
│   └── TaskbarService.cs         # Single-instance IPC, named pipe server, and taskbar geometry
│
├── ViewModels/
│   └── DownloadsViewModel.cs     # Presentation logic, search filtering, commands, and toast notifications
│
├── Views/
│   ├── DownloadsPanel.xaml       # Main floating downloads panel with responsive positioning
│   ├── DownloadsPanel.xaml.cs    # Code-behind for DWM styling and keyboard shortcuts
│   ├── SettingsWindow.xaml       # Modal settings dialog with custom chrome
│   └── SettingsWindow.xaml.cs    # Settings code-behind and folder picker
│
├── Converters/
│   ├── RelativeTimeConverter.cs  # Formats timestamps ("Just now", "5m ago", "Yesterday")
│   ├── FileSizeConverter.cs      # Formats byte counts to human-readable units (KB, MB, GB)
│   ├── CategoryColorConverter.cs # Category-specific accent color brushes
│   ├── CategoryIconGeometryConverter.cs # Vector path geometries for razor-sharp DPI scaling
│   └── BooleanToVisibilityConverter.cs  # Visibility converters with inverted mode
│
├── Helpers/
│   ├── NativeMethods.cs          # P/Invoke for Shell32, DwmApi, KnownFolders, and AppUserModelID
│   ├── DragDropHelper.cs         # Windows OS drag-and-drop initiator for DataFormats.FileDrop
│   └── RelayCommand.cs           # Standard MVVM command implementation
│
├── Assets/
│   └── Icons/
│       ├── app.ico               # Multi-resolution application icon (16x16 to 256x256)
│       └── app.png               # High-resolution raster branding asset
│
├── App.xaml                      # Application definition with OnExplicitShutdown
├── App.xaml.cs                   # Single-instance Mutex, Tray Icon setup, and lifecycle management
└── MasseterDrop.csproj           # C# 12 / .NET 8.0 Windows WPF project file
```

---

## 4. Build & Run Instructions

### Prerequisites
- Windows 10 (version 1809+) or Windows 11 (all versions).
- .NET 8.0 SDK (installed in PATH or local SDK directory).

### Building the Project
From the repository root (`C:\Users\...\Desktop\Masseter Drop`):

```powershell
# Restore and build the project in Debug mode
dotnet build

# Or publish an optimized Release build
dotnet publish MasseterDrop.csproj -c Release -r win-x64 --self-contained false -o publish
```

### Running the Application
```powershell
# Run the application directly
dotnet run

# Or launch the published executable
.\publish\MasseterDrop.exe

# To launch in background / tray mode:
.\publish\MasseterDrop.exe --minimized
```

---

## 5. How to Pin Masseter Drop to the Taskbar

1. Build or publish the application to your preferred location (e.g., `publish\MasseterDrop.exe`).
2. Right-click `MasseterDrop.exe` (or create a shortcut to it on your Desktop).
3. Select **Pin to taskbar** (Windows 10) or **Show more options -> Pin to taskbar** (Windows 11).
4. Click the pinned icon at any time to toggle the **Masseter Drop Downloads Panel**.
5. When the panel is active, clicking the icon again or clicking outside the panel will gracefully dismiss it.

---

## 6. Testing Results & Verification Matrix

An automated test suite was constructed and run via `Tests/MasseterDrop.Tests.csproj`.

| Component / Feature | Test Type | Status | Details |
|---|---|---|---|
| **Known Folders Discovery** | Automated Unit | **PASSED** | Discovered `FOLDERID_Downloads` path dynamically (`C:\Users\...\Downloads`) |
| **Extension Categorization** | Automated Unit | **PASSED** | PDF, Image, Video, Audio, ZIP, Document, Executable, Code |
| **Byte Formatting** | Automated Unit | **PASSED** | Formatted 500 B, 1.5 KB, 10 MB with invariant culture |
| **Settings Round-Trip** | Automated Integration | **PASSED** | Preserved `StartMinimized`, `MaxRecentFiles`, `Theme`, `AutoDismiss` in JSON |
| **History Service** | Automated Integration | **PASSED** | Scanned test directory, populated items, checked missing files |
| **Browser Temp File Handling** | Automated Integration | **PASSED** | Created `.crdownload`; verified file was categorized as `Downloading` and **not** marked `Completed` prematurely |
| **Download Completion** | Automated Integration | **PASSED** | Renamed `.crdownload` to `.zip`; detected completion, categorized as `Archive` |
| **Direct File Creation** | Automated Integration | **PASSED** | Created file directly; debounced, verified read access, marked completed |
| **File Deletion Tracking** | Automated Integration | **PASSED** | Deleted file; detected deletion event and updated availability |
| **Phantom & Temp Path Detection** | Automated Unit | **PASSED** | Tested `.crdownload`, `.part`, `.tmp`, `.download`, `Unconfirmed 801882`, braced GUIDs, standard GUIDs |
| **History Sanitization** | Automated Integration | **PASSED** | Verified temporary, phantom, and non-existent files are never persisted to `history.json` |
| **Legacy History Purging** | Automated Integration | **PASSED** | Verified pre-existing phantom items are sanitized on history load |
| **Single Download Single Entry** | Automated E2E | **PASSED** | Verified full Chrome download lifecycle produces exactly 1 completed item with 0 phantoms in ViewModel |
| **Simultaneous Downloads** | Automated E2E | **PASSED** | Verified 3 concurrent downloads complete with 3 distinct entries and 0 phantoms |
| **Cancelled Download Cleanup** | Automated E2E | **PASSED** | Verified deleted in-progress temp files are removed from ViewModel, not left as "Missing" |
| **Taskbar Position Clamping** | Automated Math/Screen | **PASSED** | Panel bounds strictly contained within working area (Left, Top, Right, Bottom) |
| **Single-Instance IPC** | Live Process Test | **PASSED** | Running primary instance received IPC from secondary instance; secondary instance exited code 0 |
| **OS-Level Drag-and-Drop** | Code Review & Win32 API | **VERIFIED** | Implemented using standard `DragDrop.DoDragDrop` with `DataFormats.FileDrop` |
| **System Tray Integration** | Windows Desktop Test | **VERIFIED** | Tray icon initializes with context menu (Downloads Panel, Folder, Settings, Exit) |

*Note on Unverified Items*: Multiple physical displays with varying DPI scales (e.g. 125% on Monitor 1, 200% on Monitor 2) were simulated and bounds-checked via virtual work area calculations, but require individual multi-monitor hardware setup for visual verification.

---

## 7. Known Limitations & Roadmap

- **Browser-Specific Download Progress**: In V1, browsers that do not update the file size of `.crdownload` continuously (or write chunks via private memory buffers) display indeterminate progress until the download completes and is renamed.
- **Network Drives**: Monitoring download folders hosted on certain SMB/NFS network shares may have delayed `FileSystemWatcher` change notifications depending on network server file notification support.
- **Future Enhancements (v1.1+)**:
  - Native Windows 11 Mica / Acrylic backdrop support (`DWMWA_SYSTEMBACKDROP_TYPE`).
  - Configurable multiple monitored directories (e.g. Torrent folder, Cloud drive folder).
  - Speed graph and time-remaining estimator for large multi-gigabyte downloads.

---

## 8. License

Copyright © 2026 Masseter. All rights reserved.
Distributed under the MIT License.
