# Replaces the active tabs and creates or updates the named collection.
# Existing items in a collection with the same name are preserved.

$tabs = @(
    @{
        title    = 'Microsoft Learn'
        url      = 'https://learn.microsoft.com'
        selected = $true
    },
    @{
        title = 'GitHub'
        url   = 'https://github.com'
    },
    @{
        title = 'Azure Portal'
        url   = 'https://portal.azure.com'
    }
)

& "$PSScriptRoot\Invoke-LinkScapeTabs.ps1" `
    -Mode replace `
    -CollectionName 'Work Sample' `
    -SaveState $true `
    -Tabs $tabs

