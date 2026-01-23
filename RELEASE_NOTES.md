# Release Notes

## v0.2.0 - 2026-01-23

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
