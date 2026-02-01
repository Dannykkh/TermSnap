# TermSnap - AI Terminal Assistant

> The best AI-powered SSH client + local terminal for Windows. Generate Linux commands from natural language and run AI coding tools like Claude Code with a single click.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows)
![License](https://img.shields.io/badge/License-MIT-green)

**[Korean](README.ko.md)** | English

---

## Who Is This For?

- **Struggling with Linux commands?** - Type "restart nginx" and AI generates `sudo systemctl restart nginx`
- **Managing servers frequently?** - SSH connections, file transfers, and saved commands in one place
- **Using AI coding tools?** - Run Claude Code, Aider, Gemini CLI with one click
- **Managing multiple servers?** - Mix SSH and local terminal sessions in tabs

---

## Key Features

### AI Command Generation
Convert natural language to Linux commands. Choose from Gemini, OpenAI, Claude, or Grok.

```
Input: "find partitions over 80% usage"
Output: df -h | awk '$5 > 80 {print}'
```

### High-Speed SFTP Transfer
- Single file: **100-150 MB/s** (3-5x faster)
- Parallel transfer: **400 MB/s** (4 concurrent)
- 2.5x faster than FileZilla

### One-Click AI CLI Launch
| Tool | Description |
|------|-------------|
| **Claude Code** | Anthropic's AI coding assistant |
| **Codex CLI** | OpenAI's code generator |
| **Gemini CLI** | Google's AI CLI |
| **Aider** | Open-source AI pair programming |

Auto-detect installation status, one-click install button if not installed

### AI Workflow Panel (NEW!)
Visually manage your AI coding sessions.
- **Progress Tab**: Real-time task list, ULTRAWORK mode
- **Plan Tab**: `.planning/` folder management
- **Memory Tab**: AI long-term memory (MEMORY.md) management

Details: [AI Workflow Guide](docs/AI_WORKFLOW_GUIDE.md)

---

## Installation

### Requirements
- Windows 10/11 (64-bit)
- .NET 8.0 Runtime
- AI API Key (Gemini free tier recommended)

### Option 1: Installer (Recommended)

1. Download latest version from [Releases](https://github.com/Dannykkh/TermSnap/releases)
2. Run the installer
3. Launch and enter API key in Settings

### Option 2: Build from Source

```bash
git clone https://github.com/Dannykkh/TermSnap.git
cd TermSnap
dotnet build
dotnet run --project src/TermSnap/TermSnap.csproj
```

---

## Get Started in 5 Minutes

### Step 1: Set Up API Key
- Settings (gear icon) > AI Models > Enter API Key
- [Get free Gemini API key](https://ai.google.dev/) (recommended)

### Step 2: Connect to SSH Server
- New Tab (+) > SSH Server
- Enter server details (host, username, password/SSH key)
- Ask AI: "show memory usage"

### Step 3: Local Terminal (Optional)
- New Tab (+) > Local Terminal
- Choose shell (PowerShell, CMD, WSL, Git Bash)
- Enable AI CLI and run

---

## Feature Details

### SSH Server Management
- Multiple server profiles
- SSH key authentication (.pem, .ppk support)
- Server monitoring (CPU, memory, disk)
- AI error analysis with auto-retry

### IDE-Style File Editor
- Viewer/Edit mode separation (prevent accidents)
- Syntax highlighting for 20+ languages
- Code folding, Undo/Redo
- UTF-8, EUC-KR encoding support

### Q&A Vector Search
Save frequent questions to reduce API token usage.
```
"restart nginx" > Returns saved answer instantly (no API call)
```

### Command Snippets
Save and quickly execute frequent commands.
- Category/tag organization
- Search functionality
- 11 built-in snippets

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+L` | New local terminal |
| `Ctrl+T` | New SSH connection |
| `Ctrl+Tab` | Next tab |
| `Ctrl+W` | Close tab |
| `Ctrl+S` | Save file |

---

## FAQ

**Q: Can I use it without an API key?**
A: Yes! SSH connections, file transfers, and local terminal work without API keys. Only AI command generation requires a key.

**Q: Is there a free AI API?**
A: [Gemini API](https://ai.google.dev/) offers a free tier.

**Q: Claude Code is not detected**
A: Check `claude --version` in PowerShell, then verify npm global path is in PATH.

**Q: Why is SFTP so fast?**
A: SSH.NET buffer optimization (256KB) and 4 concurrent transfers achieve up to 400MB/s.

More FAQ: [FAQ.md](docs/FAQ.md)

---

## Documentation

- [AI Workflow Guide](docs/AI_WORKFLOW_GUIDE.md) - New AI panel usage
- [User Guide](docs/USER_GUIDE.md) - Full feature documentation
- [Installation Guide](docs/INSTALLATION.md) - Detailed setup
- [FAQ](docs/FAQ.md) - Common questions

---

## Tech Stack

| Area | Technology |
|------|------------|
| Framework | .NET 8.0 / WPF |
| AI | Gemini, OpenAI, Claude, Grok |
| SSH/SFTP | SSH.NET |
| Editor | AvalonEdit |
| UI | Material Design In XAML |

---

## Contributing

Bug reports, feature suggestions, and code contributions are welcome!

1. Report bugs/suggest features in [Issues](https://github.com/Dannykkh/TermSnap/issues)
2. Fork > Branch > Commit > Pull Request

Details: [CONTRIBUTING.md](CONTRIBUTING.md)

---

## License

MIT License - Free for commercial use, modification, and distribution.

---

## Support

- [GitHub Issues](https://github.com/Dannykkh/TermSnap/issues) - Bug reports
- [GitHub Discussions](https://github.com/Dannykkh/TermSnap/discussions) - Questions & discussions

---

**If this project helps you, please give it a Star!**
