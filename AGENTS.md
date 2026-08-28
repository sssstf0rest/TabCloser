# Repository Guidelines

## Project Structure & Module Organization

- `src/TabCloser.Core/` contains platform-neutral click assembly and double-click detection logic.
- `src/TabCloser.Windows/` contains the WinForms tray app, Windows hooks, Chrome and Edge UI Automation hit-testing, and native input injection. Keep platform-specific code here.
- `tests/TabCloser.Core.Tests/` and `tests/TabCloser.Windows.Tests/` are self-contained console test runners.
- `tools/TabCloser.Diagnostics/` provides live, privacy-conscious UI Automation diagnostics.
- `icons/` holds the packaged icon; editable artwork and review renders live in `dev-kit/`. `test-fixtures/` contains manual browser fixtures, and `artifacts/` contains published output.

## Build, Test, and Development Commands

Run commands from the repository root with the .NET SDK selected by `global.json`:

```powershell
dotnet build TabCloser.sln -c Release
dotnet run --project tests/TabCloser.Core.Tests -c Release
dotnet run --project tests/TabCloser.Windows.Tests -c Release
dotnet publish src/TabCloser.Windows/TabCloser.Windows.csproj -p:PublishProfile=win-x64 -o artifacts/win-x64
```

The build treats warnings as errors. The test commands run the core and Windows-specific suites. Publishing creates the self-contained Windows executable. Use the diagnostics commands documented in `README.md` when investigating browser hit-testing.

## Coding Style & Naming Conventions

Follow the existing C# style: four-space indentation, file-scoped namespaces, braces on new lines, and nullable annotations enabled. Use `PascalCase` for types, methods, properties, and test names; use `camelCase` for parameters and locals; prefix interfaces with `I`. Prefer small immutable records/value types in Core and keep P/Invoke declarations isolated under `Interop/`. Preserve fail-safe behavior: uncertain, stale, or invalid input must result in no tab closure.

## Testing Guidelines

Tests use custom `Program.cs` runners rather than xUnit/NUnit. Add focused methods named for behavior, register each in the `tests` array, and verify both success and rejection paths. There is no numeric coverage threshold; behavioral coverage is expected for timing boundaries, interrupted input, and recovery. Complete the relevant scenarios in `MANUAL_TESTS.md` after changing hooks, UI Automation, packaging, or target frameworks.

## Commit & Pull Request Guidelines

History is minimal; use short, imperative commit subjects such as `Add desktop-switch reset handling`. Keep commits scoped and include tests with behavior changes. Pull requests should explain user-visible impact and safety implications, list automated and manual verification, link related issues, and include screenshots only for tray UI or icon changes. Do not commit generated `bin/` or `obj/` trees.
