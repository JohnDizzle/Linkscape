# Adds these tabs to the current window and creates or updates the named collection.

$tabs = @(
    @{
        title    = 'PowerShell repository'
        url      = 'https://github.com/PowerShell/PowerShell'
        selected = $true
    },
    @{
        title = 'PowerShell documentation'
        url   = 'https://learn.microsoft.com/powershell/'
    }
)

& "$PSScriptRoot\Invoke-LinkScapeTabs.ps1" `
    -Mode append `
    -CollectionName 'PowerShell Research' `
    -SaveState $true `
    -Tabs $tabs

