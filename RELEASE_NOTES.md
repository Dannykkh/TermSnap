# Release Notes

## v1.5.0 - 2026-01-26

### 🎨 IDE-Style File Editor (Major Update)

TermSnap now features a full-featured code editor powered by AvalonEdit, transforming from a terminal tool into a CLI-centric IDE.

#### Code Editor Features
- **AvalonEdit Integration**: Professional code editing experience
  - Line numbers with configurable margin
  - Syntax highlighting for 20+ languages
  - Code folding for `{}` blocks and XML tags
  - Current line highlighting
  - Dark theme optimized colors (VS Code style)

- **Edit Mode (Ctrl+E)**: Toggle between view and edit modes
  - View mode: Read-only, prevents accidental changes
  - Edit mode: Full editing with save support
  - Unsaved changes detection with confirmation dialog

- **Search & Navigation**:
  - `Ctrl+F`: Find text (AvalonEdit SearchPanel)
  - `Ctrl+H`: Find and Replace dialog
  - `Ctrl+G`: Go to line dialog
  - `Ctrl+S`: Save file

- **Font & Display**:
  - `Ctrl+Wheel`: Zoom in/out (8-32pt)
  - Word wrap toggle button
  - Consolas font for code files

#### Status Bar
- **Cursor Position**: Line and column display
- **Encoding**: UTF-8, UTF-16 detection via BOM
- **Line Ending**: CRLF/LF/CR detection
- **Word Wrap**: Toggle button with visual indicator

#### Dark Theme Syntax Colors
Optimized for dark backgrounds (VS Code inspired):
- Keywords: Blue (#569CD6)
- Strings: Orange (#CE9178)
- Comments: Green (#6A9955)
- Numbers: Light green (#B5CEA8)
- Classes/Types: Cyan (#4EC9B0)
- Methods: Yellow (#DCDCAA)

### 📝 Markdown Renderer Improvements

- **Extended Header Support**: h4 (`####`), h5 (`#####`), h6 (`######`)
- **Inline Styles in Lists**: Bold, italic, code, links now work in list items
- **Image Syntax**: `![alt](url)` displayed as placeholder text
- **Quote Inline Styles**: Blockquotes now support inline formatting

### 🖥️ Local Terminal Improvements

- **Path Display Restored**: Current directory shown in input area
  - Folder icon with path text
  - Text trimming for long paths (max 400px)

### 📚 Documentation

- **Keyboard Shortcuts Section**: Added to both English and Korean README
  - Global shortcuts (Ctrl+L, Ctrl+T, Ctrl+Tab, Ctrl+W)
  - File editor shortcuts (Ctrl+E/S/F/H/G/Z/Y)
  - Terminal shortcuts
- **File Viewer Screenshot**: New screenshot added to docs

### 🔧 Technical Improvements

#### FileViewerPanel
- `FileViewerPanel.xaml.cs`: Complete rewrite for IDE features
  - `SearchPanel.Install()`: Built-in search functionality
  - `FoldingManager`: Code folding for braces and XML
  - `ApplyDarkThemeColors()`: Custom syntax highlighting colors
  - `DetectEncoding()`: BOM-based encoding detection
  - `DetectLineEnding()`: CRLF/LF/CR detection
  - Keyboard shortcuts handler
  - Mouse wheel zoom handler

- `FileViewerPanel.xaml`: New status bar and controls
  - Status bar with cursor position, encoding, line ending
  - Word wrap toggle button
  - Save and edit toggle buttons

#### BraceFoldingStrategy
- New class for `{}` block folding
- Minimum 2-line requirement for fold creation
- Sorted folding regions for proper nesting

### 🐛 Bug Fixes
- Fixed syntax highlighting colors invisible on dark theme
- Fixed line numbers too close to code text
- Fixed markdown h4-h6 headers not rendering
- Fixed bold/italic not working inside list items
- Fixed image syntax causing render issues

### 📋 File Type Support
Now viewable in file editor:
- Text: `.txt`, `.log`, `.ini`, `.cfg`, `.conf`, `.env`
- Code: `.cs`, `.py`, `.js`, `.ts`, `.java`, `.cpp`, `.go`, `.rs`, `.rb`, `.php`
- Web: `.html`, `.css`, `.scss`, `.json`, `.xml`, `.yaml`
- Scripts: `.sh`, `.bash`, `.ps1`, `.bat`, `.cmd`
- Markup: `.md`, `.markdown`

---

## v1.4.0 - 2026-01-25

### 🎨 UI/UX Improvements

#### Tab UI Redesign
- **Fixed Tab Width**: Tabs now have consistent width (MaxWidth: 180px)
  - Prevents excessively long tabs that waste space
  - Text trimming with ellipsis for long folder names
  - Better visual consistency across multiple tabs

- **Shell Type Icons**: Visual indicators replace text labels
  - PowerShell: Blue icon
  - CMD: Gray icon
  - WSL: Orange icon
  - Git Bash: Red icon
  - SSH Server: Primary color icon

- **Hover-Only Close Button**: Improved accidental click prevention
  - X button hidden by default (opacity: 0)
  - Appears on mouse hover (opacity: 1)
  - Positioned at far right of tab
  - Reduced risk of unintentional tab closure

- **Simplified Tab Headers**: Cleaner tab text
  - Shows only folder name instead of "ShellType (folder)"
  - Status message still shows full details in status bar

#### Status Bar Optimization
- **Removed Duplicate Path Display**: Eliminated redundancy
  - Current directory already shown in input box
  - Removed from status bar to reduce clutter
  - Git branch indicator preserved

#### FileViewerPanel Integration
- **Fixed Panel Display**: Markdown viewer now works correctly
  - Fixed Grid.Column placement (Column 0 → Column 1)
  - Panel now overlays terminal area (right-aligned)
  - Proper view resolution from MainContentControl
  - Added comprehensive debug logging

- **Theme Integration**: Consistent color scheme
  - Applied dynamic theme colors to all icons
  - LocalizationService integration for text
  - Proper background transparency (95% opacity)

### ⚡ Performance Improvements

#### Output Batch Processing
- **60fps Output Limit**: Reduced UI thread load
  - Output buffered in StringBuilder
  - 16ms timer (60fps) for batch processing
  - Dramatically reduces Dispatcher.BeginInvoke calls
  - Prevents CPU spikes during high-frequency output
  - Eliminates fan noise and UI freezing

#### IME Detection Optimization
- **Event-Based IME Monitoring**: Removed polling overhead
  - Eliminated 200ms polling timer
  - Uses InputLanguageChanged event exclusively
  - Reduced main UI thread load
  - More responsive and efficient

### 🔧 Technical Improvements

#### LocalTerminalView
- `LocalTerminalView.xaml.cs`: Enhanced output handling
  - `_outputBuffer`: StringBuilder for batch accumulation
  - `_outputBatchTimer`: 16ms DispatcherTimer for 60fps rendering
  - `OnRawOutputReceived()`: Append to buffer instead of immediate dispatch
  - `OnOutputBatchTimerTick()`: Process all buffered output at once
  - Cleanup in `OnUnloaded()` for timer disposal

- `LocalTerminalView.xaml`: UI cleanup
  - Removed current directory display from status bar
  - Simplified Grid structure

#### MainWindow
- `MainWindow.xaml.cs`: Fixed FileViewerPanel access
  - Changed from `TabItem.Content` to `MainContentControl.Content`
  - Added IsSplitMode detection for future split-view support
  - Comprehensive debug logging for file opening workflow

- `MainWindow.xaml`: Tab style improvements
  - Added MaxWidth to WarpTabItemStyle
  - Redesigned ItemTemplate with Grid layout
  - Shell type icons with MultiDataTrigger styling
  - Close button with hover trigger

#### LocalTerminalViewModel
- `LocalTerminalViewModel.cs`: Simplified tab header logic
  - TabHeader shows only folder name
  - StatusMessage retains full details
  - Fixed shellName variable scope issues

#### FileViewerPanel
- `FileViewerPanel.xaml.cs`: Enhanced integration
  - Added LocalizationService import
  - Debug logging throughout OpenFileAsync workflow
  - LocalizationService for Close button text
  - TODO comments for v2.0 features

- `FileViewerPanel.xaml`: Layout fixes
  - Grid.Column="1" for proper overlay positioning
  - Added x:Name="FileViewerBorder" for code access
  - Applied theme colors to all UI elements

### 🐛 Bug Fixes
- Fixed FileViewerPanel not appearing when clicking .md files
- Fixed FileViewerPanel overlaying file tree instead of terminal
- Fixed TabItem.Content being null (wrong view access method)
- Fixed IME button not updating (removed timer-based approach)
- Fixed shellName variable scope in LocalTerminalViewModel
- Fixed CPU fan noise during terminal output
- Fixed UI freezing during high-frequency output

### 🎯 Performance
- Output rendering: Reduced from hundreds/sec to 60/sec (stable 60fps)
- IME detection: Eliminated 200ms polling overhead
- CPU usage: Significantly reduced during terminal output
- UI responsiveness: No more freezing during intensive operations

---

## v1.3.0 - 2026-01-24

### 🎉 Major Improvements

#### View Caching System
- **Tab Persistence**: Interactive mode output now persists when switching tabs
  - View caching prevents TerminalControl destruction during tab switches
  - Claude Code CLI sessions maintain state across tab navigation
  - Background output continues processing even when tab is not visible
  - Automatic buffer restoration when returning to cached views

#### Background Output Buffer
- **1MB Output Buffer**: Terminal output buffered during background operation
  - Prevents data loss when tab is not visible
  - Automatic size management with FIFO (First In, First Out) trimming
  - Seamless restoration when tab becomes active
  - Memory-efficient with configurable size limits

#### Tab Switching Performance
- **Optimized Rendering**: Dramatically improved tab switch performance
  - Eliminated redundant buffer restoration on cached views
  - Removed unnecessary terminal resizing operations
  - Smart detection of existing terminal content
  - Background-priority rendering for smooth UI experience

#### Data Receiving Indicator
- **Live Activity Spinner**: Visual indicator for active data reception
  - Rotating spinner (/, -, \, |) displayed in tab header
  - 100ms animation interval for smooth rotation
  - Auto-hide after 500ms of inactivity
  - Green color coding for easy recognition

### 🎨 UI/UX Improvements

#### Interactive Input Box
- **Copy/Paste Support**: Fixed clipboard operations in interactive mode
  - Ctrl+C: Copy selected text, or send interrupt signal if nothing selected
  - Ctrl+V: Paste text, or handle clipboard images
  - Ctrl+X: Cut selected text
  - Ctrl+A: Select all text
  - Custom CommandBindings for reliable operation

#### Cursor Selection
- **Selection Background Fix**: Improved text selection visual feedback
  - Changed selection brush from transparent white to solid blue (#1565C0)
  - Removed SelectionOpacity for cleaner appearance
  - Added LostFocus handler to clear selection artifacts
  - Better visual consistency with application theme

#### Frequent Commands Panel
- **Panel Reorganization**: Moved to MainWindow for better integration
  - Cross-display with SnippetPanel based on session type
  - Local Terminal sessions → SnippetPanel
  - SSH Server sessions (connected) → FrequentCommandsPanel
  - Unified panel management in MainWindow
  - Search and edit functionality preserved

### 🔧 Technical Improvements

#### MainWindow Architecture
- `MainWindow.xaml.cs`: Implemented view caching dictionary
  - `_viewCache`: Dictionary<ISessionViewModel, UIElement>
  - `GetOrCreateView()`: Smart view instantiation and caching
  - `UpdateSessionView()`: Efficient view assignment
  - Automatic cleanup on session close

#### LocalTerminalViewModel
- `LocalTerminalViewModel.cs`: Enhanced output handling
  - `_interactiveOutputBuffer`: StringBuilder with 1MB limit
  - `AppendToInteractiveBuffer()`: Size-limited buffer append
  - `GetInteractiveBuffer()`: Buffer retrieval for restoration
  - `StartDataReceivingSpinner()`: Visual activity indicator
  - Dual timer system for spinner animation and auto-hide

#### LocalTerminalView
- `LocalTerminalView.xaml.cs`: Optimized view lifecycle
  - `RestoreInteractiveBuffer()`: Smart buffer restoration with duplicate detection
  - `InteractiveInputTextBox_Loaded()`: CommandBindings initialization
  - `StopImeMonitoring()`: IME monitoring cleanup
  - Background-priority rendering for non-blocking updates

### 🐛 Bug Fixes
- Fixed profile editing not opening dialog (object reference mismatch)
- Fixed LogViewerWindow NullReferenceException during initialization
- Fixed tab switching causing terminal output loss in interactive mode
- Fixed duplicate buffer restoration on tab activation
- Fixed cursor background remaining after text input
- Fixed Ctrl+C/V/X not working in interactive input box

### 🏗️ Architecture Changes
- Introduced `ISessionViewModel.SpinnerText` property
- View lifecycle now managed by MainWindow instead of DataTemplateSelector
- Session-specific panels (Snippets/Frequent Commands) unified in MainWindow
- Enhanced event handling for view activation/deactivation

### 📚 Documentation
- Updated architecture notes for view caching system
- Documented tab switching performance improvements
- Added buffer management guidelines

### 🎯 Performance
- Tab switching: ~500ms → ~5ms (100x improvement)
- Memory usage: Stable with 1MB buffer cap per session
- UI responsiveness: Background-priority rendering eliminates blocking

---

## v1.2.0 - 2026-01-23

### 🎉 Major Improvements

#### Local Terminal Experience
- **Claude Code CLI Integration**: Seamless integration with Anthropic's Claude Code CLI
  - Welcome box rendering now properly displays on terminal startup
  - Fixed initial size mismatch issues that caused text misalignment
  - Automatic terminal sizing ensures Claude Code welcome box fits perfectly

#### Terminal Rendering
- **Flexible Tiered Resize System**: Terminal now resizes in smart increments
  - Column sizing: 10-column increments (e.g., 82 → 80, 88 → 90, 125 → 130)
  - Row sizing: 5-row increments (e.g., 34 → 35, 38 → 40, 43 → 45)
  - Prevents excessive resizing from small window adjustments
  - More responsive to actual window size changes
  - Min: 80×24, Max: 200×60

#### Input Method Editor (IME)
- **Auto-Updating IME Button**: Korean/English toggle button now updates automatically
  - 200ms monitoring timer tracks IME state changes
  - Visual indicator ("한" / "A") reflects current input mode
  - Works in both normal and interactive input modes

#### Keyboard Shortcuts
- **Han/Eng Key Support**: Added native Korean input toggle support
  - Handles `HangulMode` and `HanjaMode` keys
  - Right Alt key alternative for IME toggle
  - Works consistently in all input contexts

### 🔧 Technical Improvements

#### Terminal Control
- `TerminalControl.cs`: Implemented tiered resize algorithm
  - `GetTieredColumns()`: Mathematical rounding for flexible column sizing
  - `GetTieredRows()`: Mathematical rounding for flexible row sizing
  - Improved `ResizeToFit()` and `ResizeToFitImmediate()` methods
  - Enhanced debug output for resize operations

#### Local Terminal View
- `LocalTerminalView.xaml.cs`: Enhanced IME and keyboard handling
  - Added `StartImeMonitoring()` method with DispatcherTimer
  - Implemented `ToggleIme()` for programmatic IME switching
  - Added IME key handlers in `PreviewKeyDown` events
  - Improved timing for AI CLI initialization

### 🐛 Bug Fixes
- Fixed Claude Code welcome box scrolling off screen
- Fixed duplicate welcome boxes appearing on initial render
- Fixed IME button not updating when language changed via keyboard
- Fixed terminal size instability during window resizing
- Fixed race conditions in ConPTY resize events

### 📚 Documentation
- Added screenshots to README showing:
  - Session selector (Local Terminal vs SSH Server)
  - Local terminal welcome panel with shell options
  - SSH connection screen
- Updated architecture documentation
- Improved setup instructions

### 🎨 UI/UX
- More responsive terminal resizing behavior
- Better visual feedback for IME state
- Smoother AI CLI integration experience

---

## Previous Releases

See [GitHub Releases](https://github.com/Dannykkh/TermSnap/releases) for earlier versions.
