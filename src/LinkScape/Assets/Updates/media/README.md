# Update-page media

The What's New timeline in `../index.html` begins with Store version 1.0.14.
Keep each release's media in this folder so older entries remain accurate.

## Release map

### 1.0.19

- `CompactCommandRail.png` - the persistent command group shown without surrounding page content
- `SearchFilterAll.png` - current All-source Search and Filter palette
- `SearchFilterCollections.png` - Collection accordion cards and startup controls
- `SearchFilterFavorites.png` - Favorites ranked using browsing visit counts

The release page intentionally uses the current screenshot slideshow instead of the earlier command-center video. The original
`TitlebarSearchPalette.png`, `CommandCenter.mp4`, and `CommandCenter.en.vtt` files remain archived here but are not loaded by the page.

### 1.0.17 (in development)

Planned capture list, in priority order:

1. `CompactTitleBar.mp4` - resize from the full toolbar into snapped compact mode, then open the overflow icon menu
2. `SiteControls.mp4` - click the favicon, show connection details and the three permissions, change zoom, and show the clear-site-data confirmation
3. `CollectionDelete.png` - selected Collection with the icon-only delete action and confirmation dialog
4. `UpdateRestart.png` - installed-update prompt with Restart now and Later choices
5. `PageRecovery.png` - friendly page-load error state and recovery action

The first two videos tell most of the release story. The final three can remain screenshots to keep the package size controlled.

### 1.0.16

- `LinkedShareFrom.mp4` - sharing a page or Linker message from LinkScape
- `LinkedShare2.mp4` - receiving and previewing an image in LinkScape

### 1.0.15

- `Collections.png` - Collections panel
- `JumpList.png` - Windows taskbar Jump List
- `LinkedUpdater.mp4` - package update experience

### 1.0.14

- `LinkedApps.mp4` - installing and opening web apps

### Shared

- `logo.png` - favicon, page branding, and video loading placeholder

## Adding a release

1. Choose the next version when preparing a package for testers or the Store, not for every commit.
2. Bump the four-part version in `Package.appxmanifest`, for example `1.0.17.0`.
3. Add the newest timeline article first in `../index.html` with `data-version="1.0.17"`.
4. Add release screenshots or videos here using stable, descriptive names.
5. Reference the media from that release article. Do not overwrite media used by older releases.
6. Test `Updates/index.html?version=1.0.17.0`; the matching timeline entry should be marked Installed and focused.

Everything under `Assets` is already included in the package, so adding media does not require a project-file change.
