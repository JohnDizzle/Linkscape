# Copilot Instructions

## Project Guidelines
- AutoSuggestBox in this WinUI/Reactor code should not be assumed to expose SelectionStart or SelectionLength; verify control APIs before suggesting caret manipulation.
- Follow Reactor best practices with clear component boundaries and state management patterns rather than large page-level state ownership.
- Use CsWin32 APIs for Win32 interop in this project instead of manual DllImport declarations.
- Implement window placement guards based on positive on-screen coordinates in App.cs, avoiding the use of a special sentinel like -32000 from the database.
- Follow repository instruction files closely and prefer inline code changes when the project instructions call for them.