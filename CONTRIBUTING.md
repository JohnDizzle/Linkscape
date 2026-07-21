# Contributing to LinkScape

Thanks for your interest in contributing to LinkScape 🎉

We welcome bug fixes, UI/UX improvements, accessibility enhancements, and new feature proposals.

## Project Goals

LinkScape is a desktop-first WinUI 3 browser-style app focused on:

- clarity
- speed
- accessibility
- personalization

Please keep these goals in mind when proposing changes.

## Getting Started

1. Fork the repository
2. Clone your fork
3. Create a branch from `master`:
   - `feature/short-description`
   - `fix/short-description`
   - `docs/short-description`
4. Build and run the app locally
5. Open a pull request against `master`

## Development Setup

- Windows 10/11
- Visual Studio 2026 Insiders (or compatible toolchain)
- .NET 10 SDK
- WinUI / Windows App SDK workload

Build from terminal:

```powershell
dotnet build .\src\LinkScape\LinkScape.csproj
```

## Code Style

- Keep changes focused and minimal.
- Prefer clear naming over clever naming.
- Avoid unrelated refactors in feature/fix PRs.
- Preserve existing architecture patterns unless your PR explicitly improves them.
- Add comments only when they provide real context.

## UI/UX Expectations

For UI changes, align with LinkScape’s design direction:

- clear hierarchy and spacing
- visible keyboard focus
- semantic labels for controls
- accessible contrast
- good compact-rail discoverability

If your change affects UX behavior, include before/after screenshots.

## Accessibility Checklist

Before submitting, verify:

- Keyboard navigation works for your flow
- Focus visuals are visible
- Icon-only controls have accessible names/tooltips
- Text remains readable on acrylic backgrounds
- Hit targets are comfortable for pointer/touch

## Testing Notes

In your PR description, include:

- what you tested
- how you tested it
- edge cases considered
- screenshots/video for UI updates

## Reporting Bugs

When opening a bug report, include:

- clear steps to reproduce
- expected result
- actual result
- app/build version (if available)
- OS version
- screenshots or screen recording

## Feature Requests

Feature requests are welcome.  
Please explain:

- the problem
- proposed solution
- alternatives considered
- UX impact

## Security

Please do **not** open public issues for sensitive security reports.  
Instead, contact the maintainer privately through GitHub security reporting channels (if enabled) or direct maintainer contact.

## License

By contributing, you agree that your contributions are provided under the project license:
**GNU General Public License v3.0**.
