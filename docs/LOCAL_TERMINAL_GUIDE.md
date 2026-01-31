# Local Terminal Session Guide

> How to use PowerShell, CMD, WSL, and Git Bash with Warp-style UI and integrate AI coding tools on Windows

---

## Table of Contents

1. [Getting Started](#getting-started)
2. [Welcome Panel](#welcome-panel)
3. [Shell Selection](#shell-selection)
4. [Using the Terminal](#using-the-terminal)
5. [AI CLI Integration](#ai-cli-integration)
6. [Workflow Panel](#workflow-panel)
7. [Subprocess Monitor](#subprocess-monitor)
8. [File Explorer](#file-explorer)
9. [Snippet Panel](#snippet-panel)
10. [Keyboard Shortcuts](#keyboard-shortcuts)
11. [Troubleshooting](#troubleshooting)

---

## Getting Started

### What is a Local Terminal Session?

Local Terminal is a feature that allows you to use various Windows shells (PowerShell, CMD, WSL, Git Bash) in a unified interface. Inspired by Warp terminal, it offers:

- **Command Block Style**: Separate commands and outputs into blocks for better readability
- **AI CLI Integration**: One-click installation and execution of AI coding tools like Claude Code, Aider, Gemini CLI
- **Workflow Support**: Manage AI task progress, planning, and memory
- **Multi-tab**: Manage multiple terminal sessions with tabs

### Starting a New Local Terminal

1. Click **New Tab** button (+) or press `Ctrl+T`
2. Select **Local Terminal**
3. Choose a folder from the welcome panel or start a new project

---

## Welcome Panel

When you first open a local terminal, a Warp-style welcome panel appears.

### Quick Start Options

| Option | Description | Shortcut |
|--------|-------------|----------|
| **Open Folder** | Select an existing project folder | - |
| **Clone Git Repository** | Clone from GitHub/GitLab | - |
| **Home Directory** | Start from user home folder | - |

### Recent Folders

Recently used folders are automatically displayed:

```
📁 C:\Users\me\Projects\myapp
   Last used: 2 hours ago | PowerShell

📁 D:\Repositories\termsnap
   Last used: Yesterday | WSL (Ubuntu)
```

Click to automatically start with the previously used shell in that folder.

### Clone Git Repository

1. Click **Clone Git Repository**
2. Enter repository URL:
   ```
   https://github.com/username/repository.git
   ```
3. Select save location
4. Terminal automatically starts in the cloned folder

---

## Shell Selection

TermSnap automatically detects all shells installed on Windows.

### Supported Shells

| Shell | Detection Method | Features |
|-------|-----------------|----------|
| **PowerShell** | Registry + Environment Variables | Default Windows shell, powerful scripting |
| **CMD** | `%COMSPEC%` | Windows Command Prompt, excellent compatibility |
| **WSL** | `wsl.exe --list` | Use Linux commands, development environment |
| **Git Bash** | Git installation path | Unix-style commands, Git integration |

### Auto-Detection Example

When you select a folder from the welcome panel, the shell selection screen appears:

```
Available Shells:

⚡ PowerShell 7.4.0
   C:\Program Files\PowerShell\7\pwsh.exe

💻 Windows PowerShell 5.1
   C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe

🐧 WSL (Ubuntu 22.04)
   wsl.exe -d Ubuntu

🌳 Git Bash 2.42.0
   C:\Program Files\Git\bin\bash.exe

⬛ CMD (Command Prompt)
   C:\Windows\System32\cmd.exe
```

### Shell Selection Criteria

**Development Work:**
- Python/Node.js: **WSL** or **PowerShell**
- C#/.NET: **PowerShell**
- Web Development: **WSL** or **Git Bash**

**System Administration:**
- Windows Management: **PowerShell**
- Batch File Execution: **CMD**

**Git Operations:**
- **Git Bash** (Familiar with Unix commands)
- **PowerShell** (Windows integration)

---

## Using the Terminal

### Command Block Style

TermSnap displays commands in blocks like Warp:

```
┌─────────────────────────────────────┐
│ 📝 ls -la                            │ ← Command
├─────────────────────────────────────┤
│ total 48                             │
│ drwxr-xr-x  8 user  user  4096 ...  │ ← Output
│ -rw-r--r--  1 user  user  1234 ...  │
│ ...                                  │
├─────────────────────────────────────┤
│ ✓ Exit code: 0 | Duration: 0.12s    │ ← Meta info
└─────────────────────────────────────┘
```

### Block UI vs Traditional UI

**Block UI (Default):**
- Separate commands and outputs into blocks
- Copy, rerun, share buttons
- Excellent readability

**Traditional UI:**
- Traditional terminal output style
- Continuous text stream
- Faster speed

**Toggle:**
- Settings > Terminal > UI Style
- Or `Ctrl+Shift+B` (Block UI toggle)

### Directory Information Display

Current status is displayed at the top of the terminal:

```
📁 C:\Users\me\Projects\myapp
🌿 main ↑1 ↓2  ← Git branch (if exists)
⚡ PowerShell   ← Current shell
```

### Command History

**Navigate with Arrow Keys:**
- `↑` (Up Arrow): Previous command
- `↓` (Down Arrow): Next command

**History Search:**
- `Ctrl+R`: Reverse history search
- Type to display matching commands

**View Full History:**
1. `Ctrl+Shift+H` or click History button
2. Search, filter, and rerun

### Auto-completion

**Tab Completion:**
- `Tab`: Auto-complete command/path
- Display list if multiple candidates exist

**Path Completion:**
```bash
cd C:\Pro[Tab]
→ cd C:\Program Files\
```

### Copy/Paste

| Action | Shortcut | Method |
|--------|----------|--------|
| Copy | `Ctrl+C` | After selecting text |
| Paste | `Ctrl+V` | In input field |
| Select All | `Ctrl+A` | In output area |

**Copy by Block:**
- Click copy button at the top right of Command Block
- Copy command + output together

---

## AI CLI Integration

TermSnap makes it easy to install and use AI coding tools.

### Supported AI CLI Tools

| Tool | Description | Features |
|------|-------------|----------|
| **Claude Code** | Anthropic Claude-based coding agent | Natural language commands, automatic code fixes |
| **Aider** | GPT-4-based pair programming | Git integration, file editing |
| **Gemini CLI** | Google Gemini-based CLI | Fast response, code generation |
| **Codex CLI** | OpenAI Codex-based CLI | Code completion, conversion |

### One-Click Installation

If AI CLI is not installed:

1. Click **AI Tools** button
2. Select tool to install (e.g., Claude Code)
3. Click **Install** button
4. Automatic installation starts:
   ```
   📦 Installing Claude Code...
   ✓ Checking Node.js
   ✓ npm install -g @anthropic-ai/claude-code
   ✓ Installation complete!
   ```

### One-Click Execution

**Normal Mode:**
1. Click **AI Tools** button
2. Select tool (e.g., Claude Code)
3. Click **Run** button
4. Enter AI prompt:
   ```
   claude> Add login functionality to this project
   ```

**Auto Mode:**
1. Select **Auto Mode** from **AI Tools** button
2. Select and run tool
3. AI proceeds without confirmation (similar to ULTRAWORK)
4. Automatic execution until completion

### Auto Mode vs Normal Mode

| Mode | Behavior | When to Use |
|------|----------|-------------|
| **Normal** | Request confirmation at each step | Complex tasks, careful changes |
| **Auto** | Automatically complete tasks | Simple tasks, trusted commands |

**Auto Mode Precautions:**
- May automatically proceed with risky tasks
- Normal mode recommended for important projects
- Can interrupt anytime with `Ctrl+C`

### AI CLI Examples

#### Claude Code

```bash
# Code refactoring
claude> Refactor this file according to clean code principles

# Bug fixing
claude> Find and fix the NullReferenceException

# Add new features
claude> Read README.md and implement login functionality
```

#### Aider

```bash
# Edit file
aider src/app.py

# With Git commits
aider --auto-commits

# Use specific model
aider --model gpt-4
```

#### Gemini CLI

```bash
# Code generation
gemini "Create a web scraper in Python"

# Code explanation
gemini "Explain what this function does" < function.js
```

---

## Workflow Panel

A workflow panel for more effective use of AI coding tools.

### Open/Close

- Click **Workflow** button (orange)
- Panel appears on the right
- Click again to close

### Three Tabs

#### 1. Progress Tab

View AI task progress in real-time.

**Display Information:**
- **TODO List**: Task items generated by AI
- **Progress Bar**: Percentage of completed tasks
- **Current Task**: Highlight item in progress

**ULTRAWORK Button:**
- Start Claude Code's automatic work mode
- Automatically send `/ulw` command
- Automatically proceed until task completion

**Example:**
```
Progress: 3/5 (60%)

✅ Implement login API endpoint
✅ Add authentication middleware
🔄 Implement frontend login form  ← Current task
⬜ Implement session management
⬜ Write test code
```

#### 2. Planning Tab

Manage the project's `.planning/` folder.

**Features:**
- **Create Planning Folder**: Show create button if folder doesn't exist
- **File List**: Display markdown files
- **Open File**: Click to open in built-in viewer
- **Add New File**: Create planning documents

**Recommended Structure:**
```
.planning/
├── README.md           # Project overview
├── ARCHITECTURE.md     # System architecture
├── CODING_STYLE.md     # Coding conventions
├── ROADMAP.md          # Development plan
└── DECISIONS.md        # Key decisions
```

**How to Use:**
1. Create `.planning/` folder at project start
2. Write project-related documents
3. AI CLI tools automatically reference them
4. Maintain consistent development direction

#### 3. Memory Tab

Manage AI's long-term memory (`MEMORY.md`).

**Features:**
- **Search**: Filter memories by keyword
- **Type Filter**: Fact, Preference, TechStack, Project, Instruction, Lesson
- **Add Memory**: Manually add new memory
- **Delete Memory**: Remove unnecessary memories
- **Auto Record**: Enable automatic saving via hooks
- **Open File**: Directly edit `MEMORY.md`

**Memory Types:**

| Type | Description | Example |
|------|-------------|---------|
| **Fact** | Project facts | "This project uses WPF + .NET 8.0" |
| **Preference** | User preferences | "Variable names: camelCase, Methods: PascalCase" |
| **TechStack** | Technology stack | "UI: Material Design In XAML" |
| **Project** | Project information | "Deployment: Azure Web App" |
| **Instruction** | AI instructions | "Must pass build test before commit" |
| **Lesson** | Learned lessons | "This API has rate limit" |

**Example:**
```
[Fact] This app implements SSH connection with SSH.NET library
[Preference] Write comments in Korean
[TechStack] SSH: SSH.NET, SFTP: Renci.SshNet
[Instruction] All tests must pass before PR creation
[Lesson] Optimal SFTP upload buffer size is 32KB
```

### Hook Setup

Configure AI to automatically save learned content to `MEMORY.md`.

**Setup Method:**
1. Open Memory tab
2. Click **Setup Hook** button
3. Automatically create `.claude/settings.local.json`
4. Done! Workflow button stops blinking

**Why Hooks are Needed:**
- AI forgets context when session ends
- Hook setup automatically saves learned content
- Next session automatically loads previous context
- AI maintains "memory" per project

**Verify Operation:**
- Workflow button stops blinking
- Auto Record toggle can be enabled
- `MEMORY.md` automatically updates when Claude Code runs

---

## Subprocess Monitor

Monitor running AI processes and subprocesses.

### Open/Close

- Click **Process** button (green)
- Panel appears on the right
- Click again to close

### Display Information

The following information is displayed for each process:

| Item | Description | Example |
|------|-------------|---------|
| **Process Name** | Running program | claude, aider, node |
| **Command Line** | Execution arguments | `--model claude-3-sonnet` |
| **PID** | Process ID | 12345 |
| **Memory** | Memory usage | 234 MB |
| **Runtime** | Elapsed time | 00:05:23 |

### Buttons

**View Log:**
- Check process output log
- Check error messages
- Useful for debugging

**Kill:**
- Force terminate process
- Show confirmation dialog
- Remove unresponsive process

### Use Cases

**When Claude Code Seems Stuck:**
1. Open process panel
2. Check `claude` process
3. Check memory usage
4. Check log
5. Kill and restart if necessary

**Using Multiple AI Tools:**
1. Check all AI processes in process panel
2. Monitor memory usage of each tool
3. Kill some if resources are insufficient

**Debugging:**
1. Check log when error occurs
2. Check command line arguments
3. Restart process

---

## File Explorer

An integrated file explorer to browse and manage files within the terminal.

### Open/Close

- Click **File Explorer** button
- Tree panel appears on the left
- Click again to close

### Features

**File Tree:**
- Display files/folders in current working directory
- Expand/collapse folders
- Automatically filter `.git`, `node_modules`, etc.

**File Operations:**
- **Double-click**: Open file (built-in viewer)
- **Right-click**: Context menu
  - New file/folder
  - Rename
  - Delete
  - Copy path

**File Viewer:**
- **Syntax Highlighting**: Support for 20+ languages
- **Line Numbers**: Display on the left
- **Search**: Search text with `Ctrl+F`
- **Read-only**: Prevent accidental changes

**Supported File Formats:**
- **Text**: `.txt`, `.log`, `.md`
- **Code**: `.cs`, `.js`, `.py`, `.java`, `.cpp`, `.go`
- **Config**: `.json`, `.xml`, `.yaml`, `.toml`
- **Web**: `.html`, `.css`, `.scss`
- **Scripts**: `.sh`, `.ps1`, `.bat`

### Quick Navigation

**Path Input:**
```
📁 Path: C:\Users\me\Projects\myapp
```
Enter path directly to navigate to that location.

**Favorites:**
- Add frequently used folders to favorites
- Navigate with one click

### Git Integration

Additional information is displayed for Git repositories:

```
📁 myapp
├── 📄 README.md        (Modified)
├── 📄 app.js           (Staged)
├── 📄 config.json      (Untracked)
└── 📁 src/
```

**Git Status Display:**
- 🟢 Added
- 🟡 Modified
- 🔵 Staged
- ⚪ Untracked

---

## Snippet Panel

Save and quickly execute frequently used commands.

### Open/Close

- Click **Snippet** button
- Panel appears on the right
- Click again to close

### Snippet Management

**Add New Snippet:**
1. Click **New Snippet** button
2. Enter information:
   ```
   Name: Git Commit
   Command: git add . && git commit -m "Update"
   Category: Git
   Tags: git, commit
   Description: Commit all changes
   ```
3. Click **Save**

**Edit Snippet:**
1. Right-click snippet
2. Select **Edit**
3. Modify content and save

**Delete Snippet:**
1. Right-click snippet
2. Select **Delete**
3. Confirm

### Using Snippets

**Click to Execute:**
- Click snippet to automatically input command
- Press Enter to execute

**Drag and Drop:**
- Drag snippet to terminal
- Automatically input command

**Search:**
- Enter keyword in search box
- Search in name, command, tags

**Category Filter:**
- Select category to display only that category
- Example: Git, Docker, Node.js

### Recommended Snippets

#### Git
```bash
# Current branch status
git status -sb

# Check changes
git diff --stat

# Undo last commit
git reset --soft HEAD~1
```

#### Docker
```bash
# Container list
docker ps -a

# Image list
docker images

# Clean unused resources
docker system prune -a
```

#### Node.js
```bash
# Start dev server
npm run dev

# Build
npm run build

# Test
npm test -- --watch
```

---

## Keyboard Shortcuts

### Global Shortcuts

| Shortcut | Function |
|----------|----------|
| `Ctrl+T` | New tab |
| `Ctrl+W` | Close tab |
| `Ctrl+Tab` | Next tab |
| `Ctrl+Shift+Tab` | Previous tab |
| `Ctrl+1~9` | Go to specific tab |

### Terminal Shortcuts

| Shortcut | Function |
|----------|----------|
| `Enter` | Execute command |
| `Ctrl+C` | Interrupt process (when running) / Cancel input (when waiting) |
| `Ctrl+D` | End session |
| `Ctrl+L` | Clear screen |
| `↑` / `↓` | Navigate history |
| `Ctrl+R` | Search history |
| `Tab` | Auto-complete |

### Editing Shortcuts

| Shortcut | Function |
|----------|----------|
| `Ctrl+C` | Copy (when text is selected) |
| `Ctrl+V` | Paste |
| `Ctrl+X` | Cut |
| `Ctrl+A` | Select all |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |

### Panel Shortcuts

| Shortcut | Function |
|----------|----------|
| `Ctrl+Shift+W` | Toggle workflow panel |
| `Ctrl+Shift+P` | Toggle process panel |
| `Ctrl+Shift+F` | Toggle file explorer |
| `Ctrl+Shift+S` | Toggle snippet panel |
| `Ctrl+Shift+H` | Open history window |

### Search Shortcuts

| Shortcut | Function |
|----------|----------|
| `Ctrl+F` | Find in current output |
| `Ctrl+Shift+F` | Find in entire session |
| `F3` | Find next |
| `Shift+F3` | Find previous |

---

## Troubleshooting

### Shell Not Detected

**PowerShell:**
```powershell
# Check PowerShell path
(Get-Command powershell).Source

# Check if added to environment variables
$env:PATH
```

**WSL:**
```bash
# Check WSL installation
wsl --list --verbose

# Set default distribution
wsl --set-default Ubuntu
```

**Git Bash:**
```bash
# Check Git installation path
where git

# Git Bash path (typically)
C:\Program Files\Git\bin\bash.exe
```

### Command Output is Garbled

**Encoding Issue:**

PowerShell:
```powershell
# Set to UTF-8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
```

CMD:
```batch
# Code page 65001 (UTF-8)
chcp 65001
```

**TermSnap Settings:**
1. Settings > Terminal > Encoding
2. Select UTF-8

### AI CLI Won't Install

**Check Node.js:**
```bash
node --version
npm --version
```

If Node.js is not installed:
1. Download from https://nodejs.org
2. Install LTS version
3. Restart TermSnap

**Permission Issue:**

Run PowerShell as administrator:
```powershell
# Change execution policy
Set-ExecutionPolicy RemoteSigned -Scope CurrentUser

# Check npm global path
npm config get prefix
```

**Network Issue:**
```bash
# Check npm registry
npm config get registry

# Use mirror (China, etc.)
npm config set registry https://registry.npmmirror.com
```

### Workflow Panel is Empty

**No TODO File:**
- Claude Code hasn't created TODO
- Automatically created when using ULTRAWORK mode
- Can manually create `.claude/TODO.md`

**No .planning Folder:**
1. Open Planning tab
2. Click "Create Planning Folder"
3. Add documents

**No MEMORY.md:**
1. Open Memory tab
2. Add "New Memory"
3. Automatically create `MEMORY.md`

### Process Won't Terminate

**Force Kill:**
1. Open process panel
2. Select the process
3. Click **Kill** button (confirmation required)

**Kill from Task Manager:**
1. Open Task Manager with `Ctrl+Shift+Esc`
2. Find in Processes tab (e.g., claude.exe)
3. Right-click > End task

**Kill with PowerShell:**
```powershell
# Kill by PID
Stop-Process -Id 12345

# Kill by name
Stop-Process -Name "claude" -Force
```

### File Explorer is Slow

**Large Projects:**
- Enable exclusion filter for `node_modules`, `.git`, etc.
- Settings > File Explorer > Exclusion Patterns

**Large Files:**
- Files larger than 2MB are automatically excluded from viewer
- Open with external editor

### Git Information Not Displayed

**Check Git Installation:**
```bash
git --version
```

**Check Git Repository:**
```bash
# Check if current folder is Git repository
git status

# Initialize if not
git init
```

**Restart TermSnap:**
- Git information is loaded at session start
- Refresh by restarting terminal

---

## Tips and Best Practices

### Using AI CLI Effectively

**Good Prompt:**
```
"Read README.md and ARCHITECTURE.md and implement login functionality.
Use JWT token-based authentication and store sessions in Redis."
```

**Bad Prompt:**
```
"Make login"  # Too vague
```

**Provide Context:**
1. Write project documents in `.planning/` folder
2. Save important information in `MEMORY.md`
3. Communicate clear requirements

### Workflow Management

**At Project Start:**
1. Create `.planning/` folder
2. Write README.md (project overview)
3. Write ARCHITECTURE.md (structure)
4. Setup hooks (auto memory)

**During Development:**
1. Check TODO in Progress tab
2. Automatic work with ULTRAWORK
3. Check learned content in Memory tab

**After Completion:**
1. Organize `MEMORY.md`
2. Delete unnecessary memories
3. Add important lessons

### Using Snippets

**Team Sharing:**
- Export snippets as JSON
- Share with team members
- Use consistent commands

**Project-specific Snippets:**
- Create `.termsnap/snippets.json` in project root
- Automatically load per project

**Using Variables:**
```bash
# Variable: ${variable_name}
git commit -m "${message}"
```
Prompts for variable input on execution

### Multi-tab Usage

**Frontend + Backend:**
- Tab 1: Frontend dev server (`npm run dev`)
- Tab 2: Backend server (`dotnet run`)
- Tab 3: AI CLI (code modification)

**Multiple Projects:**
- Different project per tab
- Independent workflow per tab
- Integrated management in process panel

### File Explorer Usage

**Quick File Opening:**
1. Open file explorer with `Ctrl+Shift+F`
2. Double-click file
3. Check in built-in viewer

**Check Git Diff:**
1. Check modified files (🟡 indicator)
2. Open file
3. Review changes

### History Usage

**Reuse Commands:**
- Search with `Ctrl+R`
- Save frequently used commands as snippets

**Analysis:**
- Check statistics in history window
- Identify frequently used commands
- Discover inefficient tasks

---

## Related Documentation

- [SSH Server Session Guide](SSH_SESSION_GUIDE.ko.md) - Remote server connection
- [AI Workflow Guide](AI_WORKFLOW_GUIDE.md) - Detailed workflow explanation
- [User Guide](USER_GUIDE.md) - Complete feature description
- [FAQ](FAQ.md) - Frequently asked questions

---

**Version**: 1.0
**Last Updated**: 2026-01-29
**Author**: TermSnap Team
