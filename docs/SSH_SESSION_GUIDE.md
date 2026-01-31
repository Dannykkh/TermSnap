# SSH Server Session Guide

> Connect to remote Linux servers and execute commands with AI assistance

---

## Table of Contents

1. [Getting Started](#getting-started)
2. [Server Connection](#server-connection)
3. [AI Command Generation](#ai-command-generation)
4. [File Transfer (SFTP)](#file-transfer-sftp)
5. [File Editor](#file-editor)
6. [Server Monitoring](#server-monitoring)
7. [Command Management](#command-management)
8. [Troubleshooting](#troubleshooting)

---

## Getting Started

### What is an SSH Session?

An SSH session provides secure connection to remote Linux/Unix servers for command execution. TermSnap's SSH session adds AI features to traditional SSH clients:

- **Natural language commands**: "restart nginx" → `sudo systemctl restart nginx`
- **Automatic error correction**: Adds sudo on permission errors
- **Dangerous command protection**: Auto-blocks commands like `rm -rf /`

### Starting a New SSH Session

1. Click **New Tab (+)** or press `Ctrl+T`
2. Select **SSH Server Connection**
3. Choose a saved server or add a new one

---

## Server Connection

### Connect with Saved Server

If you have saved server profiles, connect with one click:

1. Select the desired server from the list
2. Click **Connect** button
3. Terminal screen appears on successful connection

### Add New Server

Click **Settings** or **Add New Server**:

```
Profile Name: Dev Server
Host: 192.168.1.100
Port: 22 (default)
Username: ubuntu
```

### Authentication Methods

#### Method 1: Password

The simplest method:
```
Auth Method: Password
Password: ********
```
- Passwords are encrypted with Windows DPAPI

#### Method 2: SSH Key

More secure method:
```
Auth Method: SSH Key
SSH Key File: C:\Users\me\.ssh\id_rsa
Passphrase: ******** (optional)
```
- `.pem` files (AWS EC2, etc.)
- `.ppk` files (PuTTY format)
- OpenSSH format (`id_rsa`)

### Server Profile Management

In **Settings**:
- Edit/delete profiles
- Set favorites (shown at top of list)
- Add notes (DB credentials, etc.)

---

## AI Command Generation

### Basic Usage

After connecting, type what you want in natural language:

```
Input: "check nginx status"
AI generates: systemctl status nginx
```

### Natural Language Command Examples

#### System Management
| Input | Generated Command |
|-------|-------------------|
| "check disk usage" | `df -h` |
| "memory usage" | `free -h` |
| "system info" | `uname -a` |
| "check uptime" | `uptime` |

#### Process Management
| Input | Generated Command |
|-------|-------------------|
| "top 5 CPU processes" | `ps aux --sort=-%cpu \| head -6` |
| "memory heavy processes" | `ps aux --sort=-%mem \| head -11` |
| "find nginx processes" | `ps aux \| grep nginx` |

#### Service Management
| Input | Generated Command |
|-------|-------------------|
| "restart nginx" | `sudo systemctl restart nginx` |
| "apache status" | `systemctl status apache2` |
| "start docker service" | `sudo systemctl start docker` |

#### Log Analysis
| Input | Generated Command |
|-------|-------------------|
| "nginx error log 50 lines" | `sudo tail -n 50 /var/log/nginx/error.log` |
| "find errors in system log" | `sudo journalctl -p err -n 50` |
| "failed SSH logins" | `sudo grep "Failed password" /var/log/auth.log` |

### Review Before Execution

When AI generates a command:

1. **Preview**: Review the generated command
2. **Edit**: Modify if needed
3. **Execute**: Press Enter or click Execute

### Automatic Error Correction

When errors occur, AI automatically attempts fixes:

```
Attempt 1: cat /var/log/nginx/access.log
Result: Permission denied

AI analyzing...

Attempt 2: sudo cat /var/log/nginx/access.log
Result: Success!
```

### Dangerous Command Protection

System-threatening commands are auto-blocked:

**Completely Blocked:**
- `rm -rf /`
- `dd if=/dev/zero of=/dev/sda`
- `:(){ :|:& };:` (fork bomb)

**Confirmation Required:**
- `rm` (delete)
- `sudo` (admin privileges)
- `reboot`, `shutdown`
- `kill`, `pkill`

---

## File Transfer (SFTP)

### Open SFTP Window

Click **File Transfer** button while connected via SSH

### File Upload

**Single file:**
1. Click **Upload** button
2. Select local file
3. Transfer starts (100-150 MB/s)

**Multiple files (parallel):**
1. Click **Upload** button
2. `Ctrl+Click` to select multiple files
3. 4 files transfer simultaneously (up to 400 MB/s)

### File Download

**Single file:**
1. Select file in remote file list
2. Click **Download** button
3. Choose save location

**Multiple files:**
1. `Ctrl+Click` or `Shift+Click` to select multiple files
2. Click **Download** button
3. Choose save folder

### File Management

- **New Folder**: Create folder on remote
- **Rename**: Rename files/folders
- **Delete**: Delete files/folders (confirmation required)

### Transfer Speed

| Mode | Speed | Comparison |
|------|-------|------------|
| Single file | 100-150 MB/s | 3-5x vs FileZilla |
| Parallel (4) | 400 MB/s | Industry leading |

---

## File Editor

### Opening Files

Double-click text file in SFTP window

### Viewer Mode (Default)

- **Read-only**: Prevents accidental edits
- **Gray background**: Indicates viewer mode
- Syntax highlighting, line numbers

### Edit Mode

1. Click **Edit** button (pencil icon)
2. Modify file content
3. **Save** (Ctrl+S) or **Cancel**

### Features

- **Syntax highlighting**: 20+ languages (C#, Python, JavaScript, JSON, XML, etc.)
- **Code folding**: Collapse `{}` blocks, XML tags
- **Line numbers**: Displayed on left
- **Encoding**: UTF-8, EUC-KR, etc.
- **Line endings**: CRLF/LF display

### Editor Shortcuts

| Shortcut | Function |
|----------|----------|
| `Ctrl+S` | Save |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |
| `Ctrl+F` | Find |
| `Ctrl+H` | Replace |
| `Ctrl+G` | Go to line |

---

## Server Monitoring

### Open Monitor Window

Click **Server Monitoring** button while connected via SSH

### Monitoring Items

- **CPU Usage**: Current CPU usage (%)
- **Memory Usage**: Used / Total
- **Disk Usage**: Per-partition usage
- **Network**: Send/receive data
- **System Info**: OS, kernel, uptime
- **Service Status**: nginx, apache, mysql, etc.
- **Top Processes**: By CPU/memory usage

### Auto Refresh

Check **Auto Refresh** to update every 10 seconds

---

## Command Management

### Command History

Click **History** button or `Ctrl+Shift+H`:

- All executed command records
- Success/failure status
- Filter by profile
- Search function

### Command Snippets

Save frequently used commands:

1. Click **Snippet Manager** button
2. Click **New Snippet**
3. Enter information:
   - Name: Restart Nginx
   - Command: `sudo systemctl restart nginx`
   - Category: Web Server
   - Tags: nginx, restart

### Q&A Knowledge Base

Save frequently asked questions and answers:

1. Click **Q&A Manager** button
2. Click **New Item**
3. Enter information:
   - Question: How to restart nginx
   - Answer: sudo systemctl restart nginx
   - Category: Web Server

**Benefits:**
- Instant answers for same questions (no API call)
- Saves API tokens
- Embedding-based similar question search

---

## Troubleshooting

### Connection Failures

**"Connection refused"**
- Verify server IP/port
- Check SSH service running: `sudo systemctl status sshd`
- Verify firewall allows port 22

**"Authentication failed"**
- Re-check username/password
- Verify SSH key file path
- Check SSH key permissions (600 on Linux)

**"Host key verification failed"**
- Occurs after server reinstall
- Delete the host entry from `~/.ssh/known_hosts`

### Command Execution Errors

**"Permission denied"**
- Check sudo privileges
- User may need sudo group membership

**"Command not found"**
- Check command path
- Package may need installation (`apt install` or `yum install`)

### File Transfer Failures

**Slow speed**
- Check network status
- Check server load
- Check for concurrent transfers

**"Permission denied" (upload)**
- Check remote directory write permissions
- Check disk space

---

## Tips

### Effective Command Requests

**Good examples:**
- "Find 500 errors in nginx error log from today"
- "Find files larger than 1GB in /var/www"

**Bad examples:**
- "Check it" (check what?)
- "Fix the problem" (what problem?)

### Using Server Notes

Add important info to profile notes:
```
MySQL Access:
- host: localhost
- user: root
- password: mysql_pass

Important Paths:
- Web root: /var/www/html
- Logs: /var/log/nginx
```

### Automate with Snippets

Save frequently used commands as snippets for one-click execution

---

**Related Documentation:**
- [Local Terminal Guide](LOCAL_TERMINAL_GUIDE.md)
- [AI Workflow Guide](AI_WORKFLOW_GUIDE.md)
- [FAQ](FAQ.md)
