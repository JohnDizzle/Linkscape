# LinkScape activation protocol samples

These samples build the active-tabs JSON package, encode it as Base64URL, and
launch the registered `link2scape://tabs/active` protocol.

## Samples

- `01-New-Collection.ps1` replaces the active tabs and creates or updates a
  collection named `Work Sample`.
- `02-Append-Tabs.ps1` appends tabs to the current window without saving them
  to a collection.
- `03-Append-To-Collection.ps1` appends tabs and creates or updates a named
  collection.
- The matching `.bat` files are convenient Task Scheduler entry points.

Edit only the `$tabs`, `-Mode`, `-CollectionName`, and `-SaveState` values in a
sample. `Invoke-LinkScapeTabs.ps1` handles JSON and Base64URL encoding.

## Mode behavior

- `replace` replaces the active tab set.
- `append` keeps the active tab set and adds the package tabs.
- `collectionName` creates the collection when needed and adds or updates the
  package URLs. It does not delete older items already in that collection.
- `saveState = $true` saves the resulting active tabs for normal startup.
- `saveState = $false` opens a temporary session without replacing saved
  startup tabs.

## Windows Task Scheduler

Create a **Start a program** action and select one of the `.bat` files as the
program/script. The `.bat` file uses `%~dp0`, so it can locate its neighboring
PowerShell sample regardless of the current working directory.

LinkScape does not need to be running. Windows starts the registered app for
the protocol; when LinkScape is already running, the activation is delivered
to that instance.
