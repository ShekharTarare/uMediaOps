# Contributing to uMediaOps

Thank you for your interest in contributing to uMediaOps!

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/YOUR-USERNAME/uMediaOps.git`
3. Create a feature branch: `git checkout -b feature/your-feature-name`
4. Make your changes
5. Test your changes thoroughly
6. Commit with clear messages: `git commit -m "Add feature: description"`
7. Push to your fork: `git push origin feature/your-feature-name`
8. Open a Pull Request

## Development Setup

### Requirements

- .NET 10.0 SDK
- Umbraco 17+
- Visual Studio 2022 or VS Code

### Building and Running

```bash
# Build the solution
dotnet build uMediaOps.sln

# Run the test site
dotnet run --project test/uMediaOps.TestSite/uMediaOpsTestSite.csproj
```

Open `https://localhost:44353/umbraco` to access the backoffice.

## Code Guidelines

### C# Code Style

- Follow standard C# naming conventions
- Use meaningful variable and method names
- Add XML documentation comments for public APIs
- Keep methods focused and single-purpose
- Use async/await for I/O operations
- Use Umbraco's `IScopeProvider` for database operations
- Use `IServiceScopeFactory` for background tasks

### JavaScript Code Style

- Use ES6+ features
- Follow LitElement conventions for web components
- Use `UmbElementMixin` for Umbraco integration
- Clean up intervals and event listeners in `disconnectedCallback()`

### Database Migrations

- Always create migrations for schema changes
- Test migrations on both SQLite and SQL Server
- Use FluentMigrator's typed API (no raw SQL Server-specific syntax)
- Check `TableExists()` before creating tables

## Pull Request Process

1. Ensure code builds without errors: `dotnet build uMediaOps.sln`
2. Test manually in the test site
3. Clearly describe what the PR does
4. Reference any related issues
5. Include screenshots for UI changes

## Reporting Issues

### Bug Reports

Include:

- Steps to reproduce
- Expected vs actual behavior
- Umbraco version and database type (SQLite/SQL Server)
- Browser console errors if applicable

### Feature Requests

Include:

- Clear description of the feature
- Use case and benefits

## Questions?

- Check the [README](README.md)
- Open a GitHub issue

## License

By contributing, you agree that your contributions will be licensed under the MIT License.
