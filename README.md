# LinkScape

[Microsof Store](https://apps.microsoft.com/detail/9NLNN451LC7T?hl=en-us&gl=US&ocid=pdpshare)

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
