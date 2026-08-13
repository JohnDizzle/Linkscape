# LinkScape Project Conventions

- Every HTML page must use `media/logo.png` for its page icon/favicon. Keep the same `media/logo.png` relative layout for new HTML pages.
- Every context-menu command must include a meaningful icon. In WinUI menus, set `MenuFlyoutItem.Icon` and prefer the shared Segoe Fluent glyphs from `BrowserConstants`.
- Production C# namespaces must mirror their feature-first folders. Do not add new types to the global namespace or create generic `Helpers`, `Utilities`, `Common`, or `Misc` folders; use the ownership rules in `docs/architecture.md`.
- Preserve LinkScape's modular, feature-first OSS architecture during every refactor. Place application lifecycle code in `Application`, browser UI in `Browser`, shared data contracts in `Models`, and feature-owned services in the matching `Services/<Feature>` module. Move files and namespaces together, update all imports and tests, and verify that no source file or behavior is lost.
- Treat `Legacy/AIChat.cs` as excluded legacy source. Do not compile, namespace-refactor, or move it back into the active application unless the user explicitly requests its restoration.
