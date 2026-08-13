# LinkScape Project Conventions

- Every HTML page must use `media/logo.png` for its page icon/favicon. Keep the same `media/logo.png` relative layout for new HTML pages.
- Every context-menu command must include a meaningful icon. In WinUI menus, set `MenuFlyoutItem.Icon` and prefer the shared Segoe Fluent glyphs from `BrowserConstants`.
- Production C# namespaces must mirror their feature-first folders. Do not add new types to the global namespace or create generic `Helpers`, `Utilities`, `Common`, or `Misc` folders; use the ownership rules in `docs/architecture.md`.
