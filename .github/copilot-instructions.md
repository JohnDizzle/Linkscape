# Copilot Instructions

## Project Guidelines
- AutoSuggestBox in this WinUI/Reactor code should not be assumed to expose SelectionStart or SelectionLength; verify control APIs before suggesting caret manipulation.
- Follow Reactor best practices with clear component boundaries and state management patterns rather than large page-level state ownership.
- Use CsWin32 APIs for Win32 interop in this project instead of manual DllImport declarations.
- Implement window placement guards based on positive on-screen coordinates in App.cs, avoiding the use of a special sentinel like -32000 from the database.
- Prefer the object-style UseState pattern when appropriate, such as `var errors = UseState<T?>(null)`, and access state through `errors.Value` and `errors.Set(...)`.
- For Microsoft UI Reactor API/source reference, use the local Git clone at `C:\win_Reactor\microsoft-ui-reactor` when needed instead of asking where Reactor is located.
- Keep the app package small: do not ship the raw Microsoft UI Reactor repo. Use the local clone for developer reference, and prefer a compact generated index later if retail/offline help is needed.
- For local Reactor reference tooling, run `tools\update-reactor.bat` to update the clone and `tools\search-reactor.bat <query>` to search it. The Command Center Chat panel should use MCP-shaped local tool boundaries that can later be exposed through Windows MCP.
- Follow repository instruction files closely and prefer inline code changes when the project instructions call for them.
- Use the MarkdownTextBlock control instead of a plain TextBlock when displaying markdown content in this project.

## MCP Organization
- Organize MCP-related service files under `src/LinkScape/Services/Mcp`, using subfolders such as Helpers, Server, and Tools when appropriate.