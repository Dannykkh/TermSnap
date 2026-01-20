# TermSnap Terminal v1.0.0 - Initial Release 🎉

> **"Making PuTTY easier"** - AI-powered terminal assistant

**Release Date**: 2025-01-18

## 🌟 Key Features

### 1. SSH Server Management
- 🔐 SSH key authentication (.pem, .ppk support)
- 📁 SFTP file transfer
- 📊 Server monitoring (CPU, memory, disk)
- 💾 Multiple server profile management

### 2. AI Command Generation
- 🤖 Natural language → Linux command conversion
- 🔄 Multiple AI providers supported:
  - Google Gemini
  - OpenAI GPT-4
  - Anthropic Claude
  - xAI Grok
- 🔍 Error analysis and automatic retry
- ⚠️ Dangerous command blocking

### 3. Q&A Vector Search (Token Saver)
- 💡 Automatic responses for frequent questions
- 🎯 Embedding-based similarity search
- 💰 Minimize API token usage

### 4. Local Terminal (Warp Style)
- 🖥️ Multiple shell support:
  - PowerShell
  - CMD
  - WSL (Windows Subsystem for Linux)
  - Git Bash
- 📂 Open folder, Git Clone
- 📋 Recent folders list

### 5. AI CLI Integration
- ⚡ One-click execution:
  - **Claude Code** - Anthropic AI coding assistant
  - **Codex CLI** - OpenAI code generation
  - **Gemini CLI** - Google AI
  - **Aider** - AI pair programming
- 🔧 Auto-detect installation
- ⚙️ Auto mode flag support
- 🎛️ Add custom CLI tools

### 6. Additional Features
- 📝 Command snippet save and management
- 📊 Command execution history
- 🌿 Automatic Git branch display
- 🎨 Dark/Light themes
- 🔒 DPAPI encryption (API keys, passwords)

## 📋 Requirements

### Required
- **OS**: Windows 10/11 (64-bit)
- **.NET Runtime**: .NET 8.0+
- **AI API Key**: At least one of: Gemini, OpenAI, Claude, or Grok

### Optional (for AI CLI)
- **Node.js**: 18+ (Claude Code, Codex, Gemini CLI)
- **Python**: 3.9+ (Aider)

## 🚀 Quick Start

### 1. Installation
1. Download installer from releases
2. Run installation wizard
3. Launch the program

### 2. AI API Key Setup
1. Settings ⚙️ → AI Models
2. Enter API key:
   - [Gemini API](https://ai.google.dev/) (Free tier available, recommended)
   - [OpenAI API](https://platform.openai.com/api-keys)
   - [Anthropic API](https://console.anthropic.com/)
   - [xAI Grok API](https://x.ai/)

### 3. First Server Connection (SSH Session)
1. "New Tab" (+) → Select "SSH Server"
2. Enter server information
3. Connect → Ask AI for commands!
   - Example: "check nginx status"
   - Example: "show disk usage"

### 4. Using Local Terminal
1. "New Tab" (+) → Select "Local Terminal"
2. Choose PowerShell/CMD/WSL/Git Bash
3. Open folder → Run AI CLI

## 📦 Download

Download from Assets below:
- **Nebula Terminal-Setup-v1.0.0.exe** (~58 MB)

## 🔧 Build from Source

```bash
git clone https://github.com/Dannykkh/nebula-terminal.git
cd nebula-terminal
dotnet restore
dotnet build
dotnet run --project src/Nebula Terminal/Nebula Terminal.csproj
```

## 📖 Documentation

- [README](https://github.com/Dannykkh/nebula-terminal#readme)
- [Contributing Guide](https://github.com/Dannykkh/nebula-terminal/blob/master/CONTRIBUTING.md)
- [Build Installer Guide](https://github.com/Dannykkh/nebula-terminal/blob/master/BUILD_INSTALLER_README.md)

## 🐛 Known Issues

No major known issues at this time.

If you find a bug, please report it in [Issues](https://github.com/Dannykkh/nebula-terminal/issues)!

## 🙏 Acknowledgements

This project uses the following open-source libraries:
- [SSH.NET](https://github.com/sshnet/SSH.NET) - SSH/SFTP
- [Material Design In XAML](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) - UI
- [sentence-transformers](https://www.sbert.net/) - Embeddings

And thanks to these AI providers:
- Google Gemini
- OpenAI
- Anthropic Claude
- xAI Grok

## 📝 Changelog

### [1.0.0] - 2025-01-18

#### Added
- Initial release
- SSH server connection and management
- AI command generation (multiple providers)
- Q&A vector search system
- Local terminal (PowerShell, CMD, WSL, Git Bash)
- AI CLI integration (Claude Code, Codex, Gemini CLI, Aider)
- Command snippet management
- Git branch display
- Dark/Light themes
- Command execution history
- SFTP file transfer
- Server monitoring

## 📬 Support

- 🐛 Bug Reports: [Issues](https://github.com/Dannykkh/nebula-terminal/issues)
- 💡 Feature Requests: [Issues](https://github.com/Dannykkh/nebula-terminal/issues)
- 💬 Discussions: [Discussions](https://github.com/Dannykkh/nebula-terminal/discussions)

---

⭐ **If this project helps you, please give it a star!**

MIT License © 2025

**Note**: This project was later rebranded as **TermSnap** in v1.1.0
