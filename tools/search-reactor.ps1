param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Query,

    [int]$MaxResults = 50
)

$reactorPath = "C:\win_Reactor\microsoft-ui-reactor"

if (-not (Test-Path $reactorPath)) {
    Write-Error "Reactor repo not found at $reactorPath"
    exit 1
}

$rg = Get-Command rg -ErrorAction SilentlyContinue
if ($rg) {
    rg --line-number --ignore-case --glob "*.cs" --glob "*.xaml" --glob "*.md" --glob "*.csproj" --max-count 5 --color never $Query $reactorPath | Select-Object -First $MaxResults
    exit $LASTEXITCODE
}

Get-ChildItem -Path $reactorPath -Recurse -File -Include *.cs,*.xaml,*.md,*.csproj |
    Select-String -Pattern $Query -SimpleMatch |
    Select-Object -First $MaxResults |
    ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
