# 🎯 Add inactive-tab media pause behavior

## Understanding
Add media control behavior so switching away from a tab auto-pauses HTML media playback, with the user-facing control placed in the tab rail item. Keep the existing tab selection flow intact and implement the pause behavior through the existing WebView instances.
## Assumptions
- Auto-pause should run whenever the selected tab changes away from the previously active tab.
- A per-tab rail control should pause media in that tab on demand; no persistent mute state is required.
- HTMLMediaElement-based playback is sufficient for the first pass.
## Approach
Update [TabView.cs](TabView.cs) to pause media in a WebView by executing script against the inactive tab's `CoreWebView2`, and call that logic when switching tabs. Also pass a pause callback into the tab rail UI so users can pause a tab directly from the rail.

Update [Browser/Components/BrowserChrome.cs](Browser/Components/BrowserChrome.cs) so expanded tab items render a small pause control in the rail item without interfering with tab selection.
## Key Files
- TabView.cs - owns tab selection and WebView instances, so it should trigger pause-on-switch and expose a pause callback
- Browser/Components/BrowserChrome.cs - renders the tab rail item and needs the pause control
## Risks & Open Questions
- Some sites use custom players that may ignore generic HTML media pause script.
- Item-level button interaction in the rail must not break tab selection behavior.

**Last Updated**: 2026-07-13 05:44:49

## 📝 Plan Steps
-  **Add WebView media pause helpers in `TabView.cs`**
-  **Wire pause-on-tab-switch and rail pause callbacks in `TabView.cs`**
-  **Add a pause button to expanded tab rail items in `BrowserChrome.cs`**
-  **Build the workspace and fix any compile issues**

