# Contributing Guide

[한국어](CONTRIBUTING.ko.md)

Thank you for your interest in contributing to TermSnap!

## How to Contribute

### 1. Issue Reports

If you find a bug or have an improvement idea:

1. Check for duplicate issues at [Issues](https://github.com/Dannykkh/TermSnap/issues)
2. Create a new issue
3. Include the following information:
   - Clear title
   - Steps to reproduce (for bugs)
   - Expected vs actual behavior
   - Environment info (Windows version, .NET version, etc.)
   - Screenshots (if applicable)

### 2. Code Contributions

#### Prerequisites

- Visual Studio 2022 or later
- .NET 8.0 SDK
- Git

#### Development Process

1. **Fork and Clone**
   ```bash
   git clone https://github.com/your-username/TermSnap.git
   cd TermSnap
   ```

2. **Create a Branch**
   ```bash
   git checkout -b feature/your-feature-name
   # or
   git checkout -b fix/bug-description
   ```

3. **Develop**
   - Follow the code style guide
   - Write meaningful commit messages
   - Test your changes

4. **Commit**
   ```bash
   git add .
   git commit -m "feat: Add new feature description"
   ```

5. **Push and Pull Request**
   ```bash
   git push origin feature/your-feature-name
   ```
   - Create a Pull Request on GitHub
   - Describe your changes in detail
   - Reference related issue numbers (#123)

## Code Style

### C# Coding Rules

```csharp
// ✅ Good example
public class GeminiService
{
    private readonly string _apiKey;

    public async Task<string> ConvertToLinuxCommand(string userRequest)
    {
        if (string.IsNullOrWhiteSpace(userRequest))
        {
            throw new ArgumentException("User request cannot be empty", nameof(userRequest));
        }

        // Logic...
    }
}

// ❌ Bad example
public class geminiservice
{
    public string apikey;

    public string convert(string s)
    {
        return ""; // No error handling
    }
}
```

### Rules

- **Naming**:
  - Classes/Methods: PascalCase
  - Variables/Parameters: camelCase
  - Private fields: _camelCase
  - Constants: UPPER_CASE

- **Formatting**:
  - Indentation: 4 spaces
  - Braces: Start on new line
  - Max line length: 120 characters

- **Comments**:
  - Use XML documentation comments
  - Add explanations for complex logic
  - Include issue numbers in TODO comments

```csharp
/// <summary>
/// Converts natural language to Linux commands using Gemini API
/// </summary>
/// <param name="userRequest">User's natural language request</param>
/// <returns>Generated Linux command</returns>
public async Task<string> ConvertToLinuxCommand(string userRequest)
{
    // TODO: #42 - Add caching feature
}
```

## Commit Message Rules

```
<type>: <subject>

<body>

<footer>
```

### Type

- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `style`: Code formatting (no functional changes)
- `refactor`: Refactoring
- `test`: Add/modify tests
- `chore`: Build/config changes

### Example

```
feat: Add command history feature

- Add CommandHistory class
- Implement history navigation with up/down arrows
- Save history to config file

Closes #42
```

## Pull Request Guidelines

### PR Title

- Clear and concise
- Follow commit message rules
- Example: `feat: Add SSH key authentication support`

### PR Description

Use the following template:

```markdown
## Changes
- Summary of changes

## Motivation
- Why is this change needed?

## Testing
- How was this tested?

## Screenshots (if applicable)
- Screenshots of UI changes

## Checklist
- [ ] Code builds successfully
- [ ] Follows style guide
- [ ] Documentation updated (if needed)
- [ ] Tests pass
```

### Review Process

1. Verify automatic build passes
2. At least 1 reviewer approval required
3. Address change requests
4. Squash and merge

## Development Environment Setup

### Recommended Tools

- **IDE**: Visual Studio 2022 Community
- **Extensions**:
  - ReSharper (optional)
  - XAML Styler
  - EditorConfig

### Build and Run

```bash
# Build
dotnet build

# Run
dotnet run --project src/TermSnap/TermSnap.csproj

# Test
dotnet test
```

## Project Structure

```
TermSnap/
├── src/TermSnap/
│   ├── Models/          # Data models
│   ├── Services/        # Business logic
│   ├── ViewModels/      # MVVM ViewModels
│   └── Views/           # UI (XAML)
├── tests/               # Unit tests
└── docs/                # Documentation
```

## Priority Features

We welcome contributions for the following features:

- [ ] macOS/Linux support (Avalonia UI migration)
- [ ] English UI localization
- [ ] Plugin system
- [ ] Cloud settings sync
- [ ] Terminal recording/playback
- [ ] Unit test coverage improvement

## Questions or Need Help?

- [GitHub Discussions](https://github.com/Dannykkh/TermSnap/discussions)
- [Issues](https://github.com/Dannykkh/TermSnap/issues)

## Code of Conduct

- Respectful and inclusive attitude
- Constructive feedback
- Welcome diverse perspectives
- Collaborative problem solving

## License

Contributed code follows the project's MIT License.

---

Thank you again for contributing! 🎉
