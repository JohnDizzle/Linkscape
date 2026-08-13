# Adds these tabs to the current LinkScape window without creating a collection.

$tabs = @(
    @{
        title    = 'PowerShell documentation'
        url      = 'https://learn.microsoft.com/powershell/'
        selected = $true
    },
    @{
        title = 'Windows App SDK documentation'
        url   = 'https://learn.microsoft.com/windows/apps/windows-app-sdk/'
    }
)

& "$PSScriptRoot\Invoke-LinkScapeTabs.ps1" `
    -Mode append `
    -SaveState $true `
    -Tabs $tabs

