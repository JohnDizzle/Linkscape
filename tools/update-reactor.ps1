param(
    [switch]$InitializeFromGit
)

$reactorPath = "C:\win_Reactor\microsoft-ui-reactor"
$repoUrl = "https://github.com/microsoft/microsoft-ui-reactor.git"
$repoPath = $reactorPath

while ($repoPath -and -not (Test-Path (Join-Path $repoPath ".git"))) {
    $parent = Split-Path $repoPath -Parent
    if ($parent -eq $repoPath) {
        $repoPath = $null
        break
    }

    $repoPath = $parent
}

if (-not $repoPath) {
    if (-not $InitializeFromGit) {
        Write-Error "Reactor source exists but is not a Git clone. Run '.\tools\update-reactor.bat -InitializeFromGit' to back it up and clone $repoUrl into $reactorPath."
        exit 1
    }

    $backupPath = "$reactorPath.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

    Write-Host "Backing up existing Reactor source to $backupPath"
    Move-Item -LiteralPath $reactorPath -Destination $backupPath

    Write-Host "Cloning $repoUrl to $reactorPath"
    git clone $repoUrl $reactorPath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Clone failed. Existing source backup remains at $backupPath."
        exit $LASTEXITCODE
    }

    $repoPath = $reactorPath
}

git -C $repoPath fetch --all --prune
git -C $repoPath pull --ff-only
git -C $repoPath log -1 --oneline
