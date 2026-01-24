# Release Notes

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
