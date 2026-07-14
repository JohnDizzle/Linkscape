# 🎯 Refactor TabView to use BrowserTabActions

## Understanding
Refactor the tab state management inside `TabView.cs` so that it uses the existing `BrowserTabActions` helper instead of duplicating add/replace/close/toggle logic inline. The goal is to make the component read more like a reducer-driven React/Reactor component without changing behavior.
## Assumptions
- `BrowserTabActions` should remain the single helper for immutable tab-array transforms.
- The user wants behavior preserved; this is a structural refactor, not a feature change.
- `TabViewPage` will still keep `UseState` as the actual state source and use `_latestTabs` only for persistence snapshots.
## Approach
Update [TabView.cs](TabView.cs) to import `AI_Agent.Browser.State` and route tab mutations through `BrowserTabActions.Replace`, `Add`, `Close`, and `ToggleFavorite`. Preserve existing selection, persistence, and WebView cleanup behavior by keeping `MarkTabsChanged`, `ScheduleTabsSave`, and close-specific host cleanup in place while delegating the immutable array transformations to the state helper.

After the refactor, validate the workspace build to ensure the updated helper usage compiles cleanly and does not require additional namespace or API adjustments.
## Key Files
- TabView.cs - contains the current inline tab mutation logic that should delegate to `BrowserTabActions`
- Browser/State/BrowserTabActions.cs - existing reducer-like helper that will become the shared tab transformation layer
## Risks & Open Questions
- `BrowserTabActions.Add` does not currently accept an explicit visit count, so callers that created a tab with `visitCount: 1` may need a follow-up update after creation to preserve behavior.
- `CloseActiveTab` includes WebView cleanup side effects that must remain outside `BrowserTabActions`.

**Last Updated**: 2026-07-13 05:07:21

## 📝 Plan Steps
-  **Refactor add and replace tab mutations in `TabView.cs`**
-  **Refactor close and favorite tab mutations in `TabView.cs`**
-  **Build the solution and fix any compile issues**

