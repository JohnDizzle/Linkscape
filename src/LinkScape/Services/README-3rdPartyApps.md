# Third-Party Web Apps / PWA Integration

This branch introduces the first implementation layer for installing supported websites and Progressive Web Apps (PWAs) in LinkScape.

## Included

- Web app manifest models.
- Manifest discovery and parsing through WebView2.
- URL, scope, icon, display-mode, and same-origin validation.
- Local SQLite persistence for installed web apps in `webapps.db`.
- Install, lookup, list, installed-state, and uninstall operations.
- Privacy-policy language covering locally stored installed-app metadata and third-party website behavior.

## Remaining UI wiring

The next integration step is to connect `WebAppManifestService.DetectAsync` to `BrowserWebViewHost.NavigationCompleted`, surface the resulting `InstallableWebApp` state through `TabViewPage`, and add an install action to `BrowserChrome`. Windows shortcut and standalone-window support can then be layered on top of `InstalledWebAppService`.

This file is intentionally included in the draft branch so the incomplete UI wiring is visible during review and is not mistaken for a finished user-facing feature.
