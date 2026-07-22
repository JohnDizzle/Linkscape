# LinkScape
<img width="1920" height="1080" alt="Screenshot (284)" src="https://github.com/user-attachments/assets/4a94c741-53d8-4ef9-8bf9-aeb2ded72a22" />

[![Download on Microsoft Store](https://img.shields.io/badge/Download-Microsoft%20Store-00A4EF?style=for-the-badge&logo=microsoft&logoColor=white)](https://apps.microsoft.com/detail/9NLNN451LC7T?hl=en-us&gl=US)

LinkScape is a WinUI 3 desktop browser-style app built on .NET 10 and Microsoft UI Reactor.

It includes a tabbed browsing shell, a command center rail, persistent history and favorites, configurable search providers, and customizable acrylic backdrop presets.

## Tech Stack

- .NET 10
- WinUI 3 / Windows App SDK
- Microsoft UI Reactor
- SQLite via `Microsoft.Data.Sqlite`
- MSIX packaging

## Current Features

- Tab-based browsing UI
- Compact and expanded left tab rail
- Command Center sections for:
  - History
  - Recent
  - Most Visited
  - Favorites
  - Settings
  - Chat placeholder
- Persistent tab state
- Persistent browsing history
- Persistent favorites store
- Search provider selection
- Acrylic backdrop gradient presets
- Windows desktop packaging via MSIX

## Design System and UX Direction

LinkScape follows a desktop-first, keyboard-friendly design with a focus on clarity, speed, and personalization.

### Design Principles

- **Clarity first**: obvious layout, clear labels, predictable behavior
- **Low friction**: common actions should require minimal clicks
- **Progressive density**: compact when needed, detailed when expanded
- **Accessible by default**: keyboard navigation, readable contrast, semantic labels
- **Personal feel**: themes and acrylic presets without sacrificing usability

### Visual Language

- **Spacing scale**: 4, 8, 12, 16, 24, 32
- **Corner radii**:
  - Small controls: 6
  - Cards/panels: 10
  - Dialogs/surfaces: 12
- **Elevation**:
  - Flat by default
  - Subtle emphasis for hover/active states
- **Typography**:
  - Title
  - Section heading
  - Body
  - Caption/meta

### Theming

LinkScape supports customizable acrylic presets. Recommended built-in preset names:

- Midnight
- Frost
- Sunset
- Graphite

Each theme should define:

- Primary accent
- Surface/background layers
- Border/subtle separator color
- High-contrast text and icon color pairs

### Information Architecture

Left rail organization:

1. **Top**: Tab actions and quick creation
2. **Middle**: Main destinations (History, Top Sites, Favorites, etc.)
3. **Bottom**: Settings and app-level actions

Command Center sections should be clearly differentiated by purpose:

- **History**: chronological navigation history
- **Top Sites**: most visited destinations
- **Recent Tabs**: recently active tab sessions
- **Favorites**: saved/pinned destinations

### Interaction Guidelines

- Always show visible keyboard focus
- Keep icon-only controls labeled with tooltips and accessible names
- Preserve discoverability in compact rail mode
- Use non-blocking loading for heavy sections (history/favorites)
- Prefer immediate shell rendering and async data hydration

### Accessibility Baseline

- Ensure contrast compliance for text over acrylic backdrops
- Provide semantic names for tab close buttons, section headers, and rail controls
- Maintain comfortable hit targets for pointer and touch
- Support full keyboard traversal for primary navigation paths

### UX Roadmap (Design)

**Now**
- Improve hierarchy and spacing consistency
- Clarify section naming (`History`, `Top Sites`, `Recent Tabs`, `Favorites`)
- Add polished empty states

**Next**
- Add first-run hints/onboarding cards
- Add tab pinning and quick tab recovery affordances
- Improve compact rail discoverability

**Later**
- Advanced tab grouping
- Optional command customization
- Expanded personalization presets

## Project Structure

```text
src/LinkScape/
  App.cs                         App bootstrap and backdrop configuration
  TabViewPage.cs                 Main browser page and state management
  Browser/
    Components/BrowserChrome.cs  Rail, title bar, command center UI
    State/BrowserTabActions.cs   Tab state transformations
  Models/BrowserTab.cs           Tab model
  Services/
    TabPersistenceService.cs     Tab persistence
    HistoryPersistenceService.cs History database
    FavoritesService.cs          Favorites database
    SettingsService.cs           Settings database
    BrowserHistoryImportService.cs Imported browser history support
  Assets/
    Help/
      helper.html                Linker help page (local MCP/AI assistant reference)
  Package.appxmanifest           MSIX manifest
```

## Requirements

- Windows 10/11
- Visual Studio 2026 Insiders or a compatible .NET / Windows App SDK toolchain
- Windows App SDK workload / WinUI desktop development support

## Getting Started

1. Open `LinkScapeSolution.slnx` in Visual Studio.
2. Restore packages.
3. Build and run the `src/LinkScape/LinkScape.csproj` project.

Or from a terminal:

```powershell
dotnet build .\src\LinkScape\LinkScape.csproj
```

## Local Data

The app creates local SQLite databases at runtime for:

- tabs
- history
- favorites
- settings

Current database files used by the app:

- `tabs.db`
- `history.db`
- `favorites.db`
- `settings.db`

### Debugging MCP and Linker Chat

MCP and Linker chat diagnostics are written to:

```text
%USERPROFILE%\Documents\LinkScapeCache\mcp-debug.log
```

Stream the log live in PowerShell with:

```powershell
Get-Content "$env:USERPROFILE\Documents\LinkScapeCache\mcp-debug.log" -Tail 100 -Wait
```

If `LINKSCAPE_CACHE_DIRECTORY` is set, use that directory instead of `Documents\LinkScapeCache`.

## Signing and Open Source Safety

Private signing assets are intentionally not stored in the repository.

- MSIX signing is disabled by default in `LinkScape.csproj`
- A sample local signing file is provided at:
  - `src/LinkScape/LinkScape.Signing.props.example`
- To sign locally or in CI:
  1. Copy that file to `src/LinkScape/LinkScape.Signing.props`
  2. Fill in your local PFX path or certificate thumbprint
  3. Keep that file out of source control

Ignored signing assets include:

- `*.pfx`
- `*.snk`
- `src/LinkScape/LinkScape.Signing.props`

## Notes

- The project targets `net10.0-windows10.0.26100.0`
- Packaging is configured for MSIX
- Some UI and accessibility cleanup items may still remain as ongoing work

## License

This project is licensed under the GNU General Public License v3.0.

See the [LICENSE](LICENSE) file for the full text.
