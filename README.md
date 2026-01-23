# TermSnap

> **AI-powered terminal assistant** - Making SSH and local development easier

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows)
![License](https://img.shields.io/badge/License-MIT-green)

**[한국어](README.ko.md)** | English

## Overview

TermSnap is a modern terminal assistant that combines AI-powered command generation, SSH/SFTP management, and local terminal integration into one unified experience. Think of it as PuTTY + AI + integrated development environment for terminal workflows.

### Key Features

- **AI Command Generation**: Convert natural language to Linux commands using Gemini, OpenAI, Claude, or Grok
- **High-Performance SFTP**: 100-150 MB/s single file, 400 MB/s parallel transfers (up to 2.5x faster than FileZilla)
- **IDE-Style File Editor**: View/edit mode separation, syntax highlighting, undo/redo support
- **Local Terminal**: PowerShell, CMD, WSL, Git Bash with AI CLI integration (Claude Code, Aider, etc.)
- **Q&A Vector Search**: Cache frequent queries to save API tokens
- **Multi-Tab Sessions**: Mix SSH and local terminal sessions in tabs
- **Dark/Light Theme**: Modern Material Design UI

## Quick Start

### Requirements

- **OS**: Windows 10/11 (64-bit)
- **.NET Runtime**: .NET 8.0+
- **AI API Key**: At least one of: Gemini, OpenAI, Claude, or Grok
- **Optional**: Node.js 18+ (for AI CLI), Python 3.9+ (for Aider)

### Installation

#### Option 1: Installer (Recommended)

1. Download the latest `.exe` from [Releases](https://github.com/Dannykkh/TermSnap/releases)
2. Run the installer
3. Launch TermSnap
4. Configure your AI API key in Settings

#### Option 2: Build from Source

```bash
# Clone the repository
git clone https://github.com/Dannykkh/TermSnap.git
cd TermSnap

# Build
dotnet build

# Run
dotnet run --project src/TermSnap/TermSnap.csproj
```

### First Steps (5 Minutes)

1. **Set up AI API Key**
   - Open Settings ⚙️ → AI Models
   - Enter your API key ([Get Gemini API key](https://ai.google.dev/) - free tier available)

2. **Connect to SSH Server**
   - Click "New Tab" (+) → "SSH Server"
   - Enter server details (host, username, password/SSH key)
   - Connect and ask AI for commands!
   - Example: "show disk usage"

3. **Use Local Terminal** (Optional)
   - Click "New Tab" (+) → "Local Terminal"
   - Choose shell: PowerShell, CMD, WSL, or Git Bash
   - Open a folder → Run AI CLI tools (Claude Code, Aider, etc.)

## Screenshots

### Session Selector

![Session Selector](docs/images/session-selector.png)

When you create a new tab, choose between:
- **Local Terminal**: Work on your local computer using PowerShell, CMD, WSL, or Git Bash
- **SSH Server Connection**: Connect to a remote Linux server via SSH

**Shortcuts**: `Ctrl+L` for Local Terminal, `Ctrl+T` for SSH Connection

### Local Terminal - Welcome Panel

![Local Terminal Welcome](docs/images/local-terminal-welcome.png)

The welcome panel lets you:
- **Select Shell**: PowerShell (default), Windows PowerShell, CMD, Git Bash, WSL (Ubuntu, Docker)
- **AI CLI Integration**: Enable Claude Code, Codex, Gemini CLI, or Aider with one checkbox
  - Auto-detect installation status
  - One-click install button if not installed
  - Auto mode toggle (`--dangerously-skip-permissions` for Claude Code)
- **Current Path Display**: Shows your working directory
- **Execute Button**: Start your selected shell with AI CLI if enabled

**Note**: Claude Code CLI requires Node.js 18+ and `npm install -g @anthropic-ai/claude-code`

### SSH Server Connection

![SSH Connection](docs/images/ssh-connection.png)

Connect to saved servers or add new ones:
- **Saved Servers**: Quick access to frequently used servers
- **Add New Server**: Configure host, port, username, password/SSH key
- **Settings**: Manage server profiles (edit, delete, organize)

**Features**:
- SSH key authentication (.pem, .ppk files supported)
- Password encryption using Windows DPAPI
- Server profiles with custom names and notes

## Features

### 1. SSH Server Management

- Multi-server profile management
- SSH key authentication (.pem, .ppk support)
- Server monitoring (CPU, memory, disk)
- AI-powered command generation with error analysis

### 2. High-Performance SFTP

**Single File Transfer:**
- Speed: 100-150 MB/s (3-5x faster than standard)
- Optimized buffer settings (maxPendingReads: 100, socket buffer: 256KB)

**Parallel Multi-File Transfer:**
- 4 concurrent transfers
- Combined speed: 400 MB/s
- Multi-select with Ctrl+Click, Shift+Click

### 3. IDE-Style File Editor

**Viewer Mode (Default):**
- Read-only with grey background
- Prevents accidental modifications
- "Edit" button to switch to edit mode

**Edit Mode:**
- Full editing capabilities
- Syntax highlighting (C#, Python, JS, JSON, XML, and more)
- Undo/Redo, line numbers, encoding selection (UTF-8, EUC-KR, etc.)
- "Save" (Ctrl+S) or "Cancel" to exit

### 4. AI CLI Integration

Run popular AI coding assistants with one click:

| CLI | Description | Auto Mode Flag |
|-----|-------------|----------------|
| **Claude Code** | Anthropic's AI coding assistant | `--dangerously-skip-permissions` |
| **Codex CLI** | OpenAI's code generation CLI | `--full-auto` |
| **Gemini CLI** | Google's Gemini AI CLI | `-y` |
| **Aider** | Open-source AI pair programming | `--yes` |
| **Custom** | Your own AI CLI | Custom |

Features:
- Auto-detect installation status
- One-click install button
- CLI-specific options (Claude: `--print`, `--verbose`, `--resume`, etc.)
- Save last selection

#### Claude Orchestration (Claude Code Only)

MCP (Model Context Protocol) based multi-agent execution:

```
Main Claude ──┬── Agent 1 (File Analysis)
              ├── Agent 2 (Code Writing)
              ├── Agent 3 (Testing)
              └── Agent 4 (Refactoring)

→ Complex tasks handled by multiple agents in parallel
```

### 5. Q&A Vector Search (Token Saver)

```
User Question: "how to restart nginx"
         ↓
Step 1: Search registered Q&A with vector embeddings
        → If match found, return instantly (no API call)
         ↓
Step 2: If no match, call AI API
        → Save result to Q&A for reuse
```

- Pre-register frequent questions/answers
- Embedding-based similarity search
- Reduce API token usage

### 6. Command Snippets

- Save frequently used commands
- Category/tag organization
- Quick search and execution

### 7. Multi-Tab Support

- Manage multiple sessions in tabs
- Mix SSH and local terminal sessions
- Independent workspace per tab

## Technology Stack

| Area | Technology |
|------|------------|
| Framework | .NET 8.0 / WPF |
| AI | Gemini, OpenAI, Claude, Grok (Multi-provider) |
| SSH/SFTP | SSH.NET, SshNet.PuttyKeyFile |
| Vector Search | Local embeddings (sentence-transformers) |
| UI | XAML, Material Design In XAML |
| Security | Windows DPAPI (Encryption) |

## Usage Examples

### SSH Server Session

```
User: "show disk usage"
AI: df -h
Output: [disk usage table]

User: "find partitions over 80% usage"
AI: df -h | awk '$5 > 80 {print}'
Output: [partition list]
```

### Local Terminal Session

```bash
# Quick AI CLI execution (from welcome panel)
> claude                                  # Run Claude Code
> claude --dangerously-skip-permissions   # Auto mode
> aider --yes                             # Aider auto mode

# Regular terminal commands
> npm run build
> git push
```

### SFTP File Transfer

**Single File:**
```
1. Click "File Transfer" button in SSH session
2. Click "Upload" → Select file (100-150 MB/s)
3. Click "Download" → Select file and save location
```

**Multiple Files (Parallel):**
```
[Upload]
1. Click "Upload"
2. Select multiple files with Ctrl+Click
3. Automatic parallel transfer (4 at a time)
   → 100 MB/s × 4 = 400 MB/s total speed

[Download]
1. Select multiple files with Ctrl+Click or Shift+Click
2. Click "Download" → Choose folder
3. Parallel download (4 at a time)
```

## Configuration

Config file location: `%APPDATA%/TermSnap/config.json`

```json
{
  "ServerProfiles": [...],
  "AIModels": [
    { "Provider": "Gemini", "ModelId": "gemini-2.0-flash", "EncryptedApiKey": "..." },
    { "Provider": "OpenAI", "ModelId": "gpt-4o", "EncryptedApiKey": "..." }
  ],
  "SelectedProvider": "Gemini",
  "SelectedModelId": "gemini-2.0-flash",
  "AICLISettings": {
    "SelectedCLI": "claude",
    "LastCommand": "claude",
    "LastAutoMode": false
  }
}
```

## AI CLI Installation

### Claude Code
```bash
npm install -g @anthropic-ai/claude-code
```
- Requirements: Node.js 18+
- [Official GitHub](https://github.com/anthropics/claude-code)

### Codex CLI
```bash
npm install -g @openai/codex
```
- Requirements: Node.js 22+
- [Official GitHub](https://github.com/openai/codex)

### Gemini CLI
```bash
npm install -g @google/gemini-cli
```
- Requirements: Node.js 18+
- [Official GitHub](https://github.com/google/gemini-cli)

### Aider
```bash
pip install aider-chat
```
- Requirements: Python 3.9+
- [Official GitHub](https://github.com/paul-gauthier/aider)

## Contributing

We welcome contributions! 🎉

1. **Bug Reports**: Report bugs in [Issues](https://github.com/Dannykkh/TermSnap/issues)
2. **Feature Requests**: Suggest new features in [Issues](https://github.com/Dannykkh/TermSnap/issues)
3. **Code Contributions**:
   ```bash
   # Fork → Clone → Branch → Commit → Push → Pull Request
   git checkout -b feature/amazing-feature
   git commit -m "Add amazing feature"
   git push origin feature/amazing-feature
   ```
4. **Documentation**: Improve README, comments, or guides
5. **Translations**: Translate to other languages

See [CONTRIBUTING.md](CONTRIBUTING.md) for details.

## FAQ

### Q: Can I use TermSnap without an AI API key?
**A**: Yes! SSH connection, file transfer, and local terminal work without an API key. Only AI command generation requires an API key.

### Q: How do I set up SSH key authentication?
**A**: Create a server profile → Select "Authentication Method" → "SSH Key" → Choose your .pem or .ppk file.

### Q: Is there a free AI API?
**A**: Yes! Gemini API has a free tier with daily limits. Get your free API key at [Google AI Studio](https://ai.google.dev/).

### Q: Why is SFTP transfer so fast?
**A**:
- **Single file**: Optimized SSH.NET library settings (maxPendingReads: 100, socket buffer: 256KB) provide 3-5x speed improvement
- **Multiple files**: 4 concurrent transfers achieve combined 400 MB/s
- Competitive with FileZilla (161 MB/s) and WindTerm (216 MB/s)

### Q: Are my API keys secure?
**A**: Yes, all API keys and passwords are encrypted using Windows DPAPI (Data Protection API) and stored securely.

## Troubleshooting

### Build Errors
- Verify .NET 8.0 SDK is installed: `dotnet --version`
- Restore dependencies: `dotnet restore`

### Runtime Errors
- Verify .NET 8.0 Runtime is installed
- [Download .NET 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)

### SSH Connection Failed
- Check firewall (allow port 22)
- Verify SSH service is running: `sudo systemctl status sshd`
- Verify host/port/credentials

## Roadmap

- [ ] macOS/Linux support (Avalonia UI migration)
- [ ] English UI localization
- [ ] Plugin system
- [ ] Cloud settings sync
- [ ] Remote desktop integration
- [ ] Terminal recording/playback

## License

**MIT License**

TermSnap is an open-source project distributed under the MIT License.

```
Copyright (c) 2026 Dannykkh

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
```

See [LICENSE](LICENSE) for full details.

## Acknowledgements

This project uses the following open-source libraries:
- [SSH.NET](https://github.com/sshnet/SSH.NET) - SSH/SFTP connections
- [Material Design In XAML](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) - UI design
- [sentence-transformers](https://www.sbert.net/) - Embedding models

Thanks to these AI providers:
- Google Gemini
- OpenAI
- Anthropic Claude
- xAI Grok

## Support

- 🐛 Bug Reports: [Issues](https://github.com/Dannykkh/TermSnap/issues)
- 💡 Feature Requests: [Issues](https://github.com/Dannykkh/TermSnap/issues)
- 💬 Discussions: [Discussions](https://github.com/Dannykkh/TermSnap/discussions)

---

⭐ If this project helps you, please give it a star!
