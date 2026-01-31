# AI Workflow Guide

> How to use the workflow panel for more effective AI coding with Claude Code, Aider, and more

---

## Table of Contents

1. [Overview](#overview)
2. [Toolbar Buttons](#toolbar-buttons)
3. [Workflow Panel](#workflow-panel)
   - [Progress Tab](#progress-tab)
   - [Plan Tab](#plan-tab)
   - [Memory Tab](#memory-tab)
4. [Subprocess Monitor](#subprocess-monitor)
5. [Hook Setup](#hook-setup)
6. [ULTRAWORK Mode](#ultrawork-mode)
7. [Tips and Best Practices](#tips-and-best-practices)

---

## Overview

The AI Workflow Panel is inspired by [oh-my-claude-code](https://github.com/anthropics/claude-code). When using AI coding tools like Claude Code:

- **Track task progress** in real-time
- **Manage planning documents** at a glance
- **Maintain AI memory** across sessions
- **Monitor running processes**

All managed conveniently through a GUI.

---

## Toolbar Buttons

Two buttons appear at the top of the local terminal screen:

| Button | Color | Function |
|--------|-------|----------|
| **Workflow** | Orange | Toggle Progress/Plan/Memory panel |
| **Process** | Green | Toggle subprocess monitor |

**When hooks are not configured**: The workflow button blinks to alert you.

---

## Workflow Panel

Click the Workflow button (orange) to open the panel on the right. Use the tabs at the top to switch between three functions.

### Progress Tab

Track AI coding tool task progress in real-time.

**Key Features:**
- **Task List**: Shows TODO items created by Claude Code
- **Progress Bar**: Visual display of completion percentage
- **ULTRAWORK Button**: Start intensive work mode

**How to Use:**
1. TODO files are automatically detected while Claude Code runs
2. Progress updates in real-time as tasks complete
3. Completed tasks are marked with checkmarks

### Plan Tab

Manage your project's `.planning/` folder.

**Key Features:**
- **Create Planning Folder**: Shows create button if folder doesn't exist
- **File List**: Displays markdown files in `.planning/`
- **Open Files**: Click to open in built-in viewer

**What is the .planning folder?**
A folder that provides project context to AI coding tools:
- `README.md` - Project overview
- `ARCHITECTURE.md` - System structure
- `ROADMAP.md` - Development plan
- Other reference documents

**How to Use:**
1. Click "Create Planning Folder"
2. Add project documents to the folder
3. Claude Code automatically references them

### Memory Tab

Manage AI long-term memory (MEMORY.md).

**Key Features:**
- **Search**: Filter memories by keyword
- **Type Filter**: Fact, Preference, TechStack, Project, Instruction, Lesson
- **Add Memory**: Manually add new memories
- **Delete Memory**: Remove selected memories
- **Auto Record**: Enable automatic memory via hooks
- **Open File**: Edit MEMORY.md directly

**Memory Types Explained:**
| Type | Description | Example |
|------|-------------|---------|
| Fact | Project facts | "This project uses .NET 8.0" |
| Preference | User preferences | "Write comments in English" |
| TechStack | Technology stack | "Frontend: React + TypeScript" |
| Project | Project info | "Deploy environment: AWS EC2" |
| Instruction | AI instructions | "Run tests before creating PR" |
| Lesson | Learned lessons | "This API has rate limits" |

---

## Subprocess Monitor

Click the Process button (green) to view running AI processes.

**Information Displayed:**
- Process name (claude, aider, etc.)
- Command-line arguments
- PID
- Memory usage
- Running time

**Buttons:**
- **Log**: View process output log
- **Terminate**: Force stop the process

**Use Cases:**
- Check status when Claude Code seems stuck
- Monitor multiple AI processes simultaneously
- Force terminate and restart when issues occur

---

## Hook Setup

Hooks are scripts that AI tools automatically run after specific events. TermSnap configures hooks to automatically save AI learnings to MEMORY.md.

### Why Hooks Are Needed

Claude Code forgets context when a session ends. With hooks:
- AI learnings are automatically saved to MEMORY.md
- Previous context is automatically loaded in the next session
- AI maintains "memory" per project

### How to Set Up Hooks

**Method 1: Via Workflow Panel**
1. Click Workflow button (orange)
2. Select "Memory" tab
3. Click "Hook Setup" button
4. Done! Button stops blinking

**Method 2: Manual Setup**

Create `.claude/settings.local.json` in your project root:

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": ".*",
        "hooks": [
          {
            "type": "command",
            "command": "node /path/to/memory-hook.js"
          }
        ]
      }
    ]
  }
}
```

### Verifying Hook Setup

After setup:
- Workflow button stops blinking
- "Auto Record" toggle becomes available in Memory tab
- MEMORY.md updates automatically when Claude Code runs

---

## ULTRAWORK Mode

ULTRAWORK is oh-my-claude-code's intensive work mode. Claude Code completes tasks without asking for intermediate confirmations.

### How to Start

1. Workflow Panel > Progress Tab
2. Click "Start ULTRAWORK" button
3. `/ulw` command is sent to Claude Code
4. Task progress displays in real-time

### ULTRAWORK Characteristics

- **Automatic Task Division**: Large tasks split into TODO items
- **Sequential Execution**: Each TODO completed in order
- **Minimal Confirmation**: Only dangerous operations ask for confirmation
- **Progress Display**: Shows completion percentage

### Cautions

- Dangerous commands still require confirmation
- Press `Ctrl+C` in terminal to stop if task goes wrong
- Test with small tasks first

---

## Tips and Best Practices

### Effective Memory Management

**Good Memory Examples:**
```
[Fact] This project uses WPF + .NET 8.0
[Preference] Variables use camelCase, methods use PascalCase
[TechStack] UI: Material Design In XAML, SSH: SSH.NET
[Instruction] Always run build test before commit
```

**Memories to Avoid:**
```
[Fact] Today's date is January 15, 2024  # Changing info
[Preference] Don't modify this file  # Too specific
```

### Planning Folder Structure

Recommended:
```
.planning/
  README.md          # Project overview
  ARCHITECTURE.md    # System structure
  CODING_STYLE.md    # Coding conventions
  ROADMAP.md         # Development plan
  DECISIONS.md       # Key decisions
```

### Workflow Examples

**New Feature Development:**
1. Add feature spec to `.planning/`
2. Start Claude Code
3. Begin implementation with ULTRAWORK
4. Check TODOs in Progress tab
5. Review learnings in Memory tab after completion

**Bug Fixing:**
1. Search related existing memories in Memory tab
2. Start Claude Code
3. Describe problem and request fix
4. Save solution as memory if useful

### Using Process Monitor

- Check memory usage when Claude Code takes long
- Distinguish processes when working on multiple projects
- Check logs before terminating/restarting on issues

---

## Troubleshooting

### Hooks Not Working

1. Check `.claude/settings.local.json` file
2. Verify file contains valid JSON
3. Restart Claude Code
4. Click "Hook Setup" again in Workflow panel

### MEMORY.md Not Updating

1. Check "Auto Record" toggle is on
2. Verify hooks are set up
3. Confirm Claude Code actually learned something

### Nothing Shows in Progress Tab

1. Check if Claude Code created TODO files
2. Verify you're in ULTRAWORK mode
3. Check for `.claude/` directory in project folder

---

## Related Documentation

- [User Guide](USER_GUIDE.md) - Full feature documentation
- [FAQ](FAQ.md) - Frequently asked questions
- [oh-my-claude-code](https://github.com/anthropics/claude-code) - Original project

---

**Version**: 1.0
**Last Updated**: 2026-01-29
