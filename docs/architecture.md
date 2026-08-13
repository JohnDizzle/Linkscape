# LinkScape architecture

LinkScape uses a feature-first source layout. A folder represents an owned
application capability, and its namespace mirrors that folder. Technical
suffixes such as `Service`, `Store`, and `Client` describe a type's role inside
the feature; they are not reasons to place unrelated types in one directory.

## Source layout

```text
src/LinkScape/
  Application/       App shell, window lifecycle, loading, and error surfaces
  Browser/           Browser shell, components, state, and presentation
  Legacy/            Explicitly excluded code retained for reference
  Services/
    Application/     Activation, updates, notices, and jump lists
    Browsing/        Address search, navigation, browser extensions
    Collections/     Named tab collections
    Favorites/       Favorites persistence and import
    History/         History persistence, import, and reports
    Infrastructure/  Settings and local storage primitives
    Linker/           Assistant providers, chat, tools, and safety
    Mcp/
      Diagnostics/   MCP-specific diagnostics
      Protocol/      MCP transport framing and messages
      Server/        Local MCP server host
      Tools/         MCP tool catalog and routing
    Sharing/         Windows share source and target integration
    Tabs/            Active-tab persistence
    WebApps/         Web app manifests, installation, state, and windows
  Models/            Models shared by more than one feature
```

## Namespace rules

- Namespaces mirror source folders beneath `src/LinkScape`.
- Feature-owned models stay with their feature. `Models` is only for types used
  across multiple features.
- New production types must not be placed in the global namespace.
- Avoid generic folders named `Helpers`, `Utilities`, `Common`, or `Misc`.
  Name a module for the capability it owns, such as `Mcp.Protocol` or
  `History.Import`.
- A feature can depend on `Infrastructure`; infrastructure must not depend on
  UI components or feature presentation.
- UI components call feature APIs. They do not issue SQL or read persistence
  files directly.

## Refactoring policy

Structural changes should preserve behavior, public type names, persisted
database schemas, settings keys, activation routes, and MCP tool contracts.
Move one coherent module at a time and keep the solution buildable between
moves. Add or update tests before changing behavior inside a relocated module.
