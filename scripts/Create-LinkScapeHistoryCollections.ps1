[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [int]$HistoryLimit = 600,
    [ValidateRange(1, 10)]
    [int]$ItemsPerCollection = 10,
    [string]$CollectionPrefix = "Smart - ",
    [string]$CacheDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-LinkScapeCacheDirectory {
    param([string]$Override)

    if (-not [string]::IsNullOrWhiteSpace($Override)) {
        return $Override
    }

    if (-not [string]::IsNullOrWhiteSpace($env:LINKSCAPE_CACHE_DIRECTORY)) {
        return $env:LINKSCAPE_CACHE_DIRECTORY
    }

    return Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)) "LinkScapeCache"
}

function Add-SqliteAssemblies {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $candidateRoots = @(
        (Join-Path $repoRoot "src\LinkScape\bin\x64\Debug\net10.0-windows10.0.26100.0"),
        (Join-Path $repoRoot "src\LinkScape\bin\Debug\net10.0-windows10.0.26100.0\win-x64"),
        (Join-Path $repoRoot "src\LinkScape\bin\Release\net10.0-windows10.0.26100.0\win-x64")
    )

    $assemblyRoot = $candidateRoots | Where-Object {
        Test-Path (Join-Path $_ "Microsoft.Data.Sqlite.dll")
    } | Select-Object -First 1

    if ($null -eq $assemblyRoot) {
        throw "Could not find Microsoft.Data.Sqlite.dll. Build LinkScape once, then run this script again."
    }

    $assemblyNames = @(
        "SQLitePCLRaw.core.dll",
        "SQLitePCLRaw.provider.e_sqlite3.dll",
        "SQLitePCLRaw.batteries_v2.dll",
        "Microsoft.Data.Sqlite.dll"
    )

    foreach ($assemblyName in $assemblyNames) {
        $assemblyPath = Join-Path $assemblyRoot $assemblyName
        if (-not (Test-Path $assemblyPath)) {
            throw "Missing SQLite assembly: $assemblyPath"
        }

        Add-Type -Path $assemblyPath
    }

    [SQLitePCL.Batteries_V2]::Init()
}

function New-SqliteConnection {
    param([string]$DatabasePath)

    $builder = [Microsoft.Data.Sqlite.SqliteConnectionStringBuilder]::new()
    $builder.DataSource = $DatabasePath
    $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new($builder.ToString())
    $connection.Open()
    return $connection
}

function Invoke-NonQuery {
    param(
        [Microsoft.Data.Sqlite.SqliteConnection]$Connection,
        [string]$Sql,
        [hashtable]$Parameters = @{}
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    foreach ($key in $Parameters.Keys) {
        [void]$command.Parameters.AddWithValue($key, $Parameters[$key])
    }

    [void]$command.ExecuteNonQuery()
    $command.Dispose()
}

function Invoke-Scalar {
    param(
        [Microsoft.Data.Sqlite.SqliteConnection]$Connection,
        [string]$Sql,
        [hashtable]$Parameters = @{}
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    foreach ($key in $Parameters.Keys) {
        [void]$command.Parameters.AddWithValue($key, $Parameters[$key])
    }

    $value = $command.ExecuteScalar()
    $command.Dispose()
    return $value
}

function Initialize-CollectionsDatabase {
    param([Microsoft.Data.Sqlite.SqliteConnection]$Connection)

    Invoke-NonQuery $Connection @"
CREATE TABLE IF NOT EXISTS TabCollections(
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL COLLATE NOCASE,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_TabCollections_Name
ON TabCollections(Name COLLATE NOCASE);

CREATE TABLE IF NOT EXISTS TabCollectionItems(
    Id TEXT PRIMARY KEY,
    CollectionId TEXT NOT NULL,
    Url TEXT NOT NULL,
    Title TEXT NOT NULL,
    SortOrder INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    FOREIGN KEY(CollectionId) REFERENCES TabCollections(Id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_TabCollectionItems_Collection_Url
ON TabCollectionItems(CollectionId, Url);

CREATE INDEX IF NOT EXISTS IX_TabCollectionItems_Collection_Order
ON TabCollectionItems(CollectionId, SortOrder);
"@
}

function Get-HistoryItems {
    param(
        [Microsoft.Data.Sqlite.SqliteConnection]$Connection,
        [int]$Limit
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = @"
SELECT Url, Title, FirstVisitedAt, LastVisitedAt, VisitCount
FROM HistoryItems
ORDER BY VisitCount DESC, LastVisitedAt DESC
LIMIT `$limit;
"@
    [void]$command.Parameters.AddWithValue('$limit', $Limit)

    $items = New-Object System.Collections.Generic.List[object]
    $reader = $command.ExecuteReader()
    while ($reader.Read()) {
        $items.Add([pscustomobject]@{
            Url = $reader.GetString(0)
            Title = $reader.GetString(1)
            FirstVisitedAt = [DateTime]::Parse($reader.GetString(2), [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
            LastVisitedAt = [DateTime]::Parse($reader.GetString(3), [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
            VisitCount = $reader.GetInt32(4)
        })
    }

    $reader.Dispose()
    $command.Dispose()
    return $items
}

function Get-FavoriteItems {
    param([Microsoft.Data.Sqlite.SqliteConnection]$Connection)

    $command = $Connection.CreateCommand()
    $command.CommandText = @"
SELECT Url, Title, UpdatedAt
FROM Favorites
ORDER BY UpdatedAt DESC, Title COLLATE NOCASE, Url COLLATE NOCASE;
"@

    $items = New-Object System.Collections.Generic.List[object]
    $reader = $command.ExecuteReader()
    while ($reader.Read()) {
        $items.Add([pscustomobject]@{
            Url = $reader.GetString(0)
            Title = $reader.GetString(1)
            UpdatedAt = [DateTime]::Parse($reader.GetString(2), [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
        })
    }

    $reader.Dispose()
    $command.Dispose()
    return $items
}

function Get-HostText {
    param([string]$Url)

    try {
        return ([Uri]$Url).Host.ToLowerInvariant()
    }
    catch {
        return $Url.ToLowerInvariant()
    }
}

function Test-PatternMatch {
    param(
        [object]$HistoryItem,
        [string[]]$Patterns
    )

    $haystack = "$(Get-HostText $HistoryItem.Url) $($HistoryItem.Url)".ToLowerInvariant()
    foreach ($pattern in $Patterns) {
        if ($haystack -match $pattern) {
            return $true
        }
    }

    return $false
}

function Test-UsableUrl {
    param(
        [string]$Url,
        [string]$Title = ""
    )

    $hostName = Get-HostText $Url
    $text = "$hostName $Url $Title".ToLowerInvariant()
    $blockedHosts = @(
        "accounts.google.com",
        "login.microsoftonline.com",
        "login.live.com",
        "oauth.telegram.org"
    )
    $blockedPatterns = @(
        "/signin",
        "/login",
        "/oauth",
        "/authorize",
        "/auth/login",
        "two-step verification",
        "2-step verification",
        "passkey",
        "select_account"
    )

    if ($blockedHosts -contains $hostName) {
        return $false
    }

    foreach ($pattern in $blockedPatterns) {
        if ($text.Contains($pattern, [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
    }

    return $true
}

function Get-CandidateScore {
    param([object]$Candidate)

    $daysOld = [Math]::Max(0, ([DateTime]::Now - $Candidate.LastVisitedAt).TotalDays)
    $recencyBoost = [Math]::Max(0, 30 - $daysOld) / 30
    $favoriteBoost = if ($Candidate.IsFavorite) { 30 } else { 0 }
    $bothSourceBoost = if ($Candidate.IsFavorite -and $Candidate.IsHistory) { 12 } else { 0 }
    return ($Candidate.VisitCount * 10) + $favoriteBoost + $bothSourceBoost + ($recencyBoost * 8)
}

function Get-OrCreateCollection {
    param(
        [Microsoft.Data.Sqlite.SqliteConnection]$Connection,
        [string]$Name
    )

    $existingId = Invoke-Scalar $Connection `
        "SELECT Id FROM TabCollections WHERE Name = `$name COLLATE NOCASE LIMIT 1;" `
        @{ '$name' = $Name }
    $now = [DateTime]::Now.ToString("O")

    if ($null -ne $existingId -and -not [Convert]::IsDBNull($existingId)) {
        Invoke-NonQuery $Connection `
            "UPDATE TabCollections SET UpdatedAt = `$updatedAt WHERE Id = `$id;" `
            @{ '$id' = [string]$existingId; '$updatedAt' = $now }
        return [string]$existingId
    }

    $id = [Guid]::NewGuid().ToString("N")
    Invoke-NonQuery $Connection `
        "INSERT INTO TabCollections(Id, Name, CreatedAt, UpdatedAt) VALUES(`$id, `$name, `$createdAt, `$updatedAt);" `
        @{ '$id' = $id; '$name' = $Name; '$createdAt' = $now; '$updatedAt' = $now }
    return $id
}

function Add-CollectionItem {
    param(
        [Microsoft.Data.Sqlite.SqliteConnection]$Connection,
        [string]$CollectionId,
        [object]$HistoryItem
    )

    $existingId = Invoke-Scalar $Connection `
        "SELECT Id FROM TabCollectionItems WHERE CollectionId = `$collectionId AND Url = `$url LIMIT 1;" `
        @{ '$collectionId' = $CollectionId; '$url' = $HistoryItem.Url }
    $sortOrder = Invoke-Scalar $Connection `
        "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM TabCollectionItems WHERE CollectionId = `$collectionId;" `
        @{ '$collectionId' = $CollectionId }
    $now = [DateTime]::Now.ToString("O")
    $safeTitle = if ([string]::IsNullOrWhiteSpace($HistoryItem.Title)) { $HistoryItem.Url } else { $HistoryItem.Title.Trim() }
    $id = if ($null -ne $existingId -and -not [Convert]::IsDBNull($existingId)) { [string]$existingId } else { [Guid]::NewGuid().ToString("N") }

    Invoke-NonQuery $Connection @"
INSERT INTO TabCollectionItems(Id, CollectionId, Url, Title, SortOrder, CreatedAt, UpdatedAt)
VALUES(`$id, `$collectionId, `$url, `$title, `$sortOrder, `$createdAt, `$updatedAt)
ON CONFLICT(CollectionId, Url) DO UPDATE SET
    Title = excluded.Title,
    UpdatedAt = excluded.UpdatedAt;
"@ @{
        '$id' = $id
        '$collectionId' = $CollectionId
        '$url' = $HistoryItem.Url
        '$title' = $safeTitle
        '$sortOrder' = [int]$sortOrder
        '$createdAt' = $now
        '$updatedAt' = $now
    }
}

$cachePath = Get-LinkScapeCacheDirectory $CacheDirectory
$historyDbPath = Join-Path $cachePath "history.db"
$favoritesDbPath = Join-Path $cachePath "favorites.db"
$collectionsDbPath = Join-Path $cachePath "tabCollections.db"

if (-not (Test-Path $historyDbPath)) {
    throw "History database was not found: $historyDbPath"
}

if (-not (Test-Path $favoritesDbPath)) {
    throw "Favorites database was not found: $favoritesDbPath"
}

Add-SqliteAssemblies

$categories = @(
    [pscustomobject]@{
        Name = "${CollectionPrefix}AI & Research"
        Patterns = @("openai", "chatgpt", "copilot", "anthropic", "claude", "huggingface", "arxiv", "paper", "llm", "model", "\bai\b", "generative-ai")
    },
    [pscustomobject]@{
        Name = "${CollectionPrefix}Dev & Docs"
        Patterns = @("github", "gitlab", "stackoverflow", "stackexchange", "docs\.", "developer", "dotnet", "nuget", "npmjs", "react", "typescript", "powershell")
    },
    [pscustomobject]@{
        Name = "${CollectionPrefix}Microsoft & Cloud"
        Patterns = @("microsoft", "azure", "portal\.azure", "learn\.microsoft", "windows", "office", "copilotstudio")
    },
    [pscustomobject]@{
        Name = "${CollectionPrefix}News & Reading"
        Patterns = @("news", "msn", "cnn", "foxnews", "nbcnews", "cbsnews", "abcnews", "nytimes", "washingtonpost", "theverge", "tmz", "aol", "yahoo")
    },
    [pscustomobject]@{
        Name = "${CollectionPrefix}Media & Communities"
        Patterns = @("youtube", "youtu\.be", "music\.youtube", "twitch", "spotify", "netflix", "hulu", "disneyplus", "imdb", "rottentomatoes", "reddit", "tiktok", "instagram", "discord", "x\.com")
    }
)

$historyConnection = New-SqliteConnection $historyDbPath
try {
    $historyItems = @(Get-HistoryItems $historyConnection $HistoryLimit | Where-Object { Test-UsableUrl $_.Url $_.Title })
}
finally {
    $historyConnection.Dispose()
}

$favoritesConnection = New-SqliteConnection $favoritesDbPath
try {
    $favoriteItems = @(Get-FavoriteItems $favoritesConnection | Where-Object { Test-UsableUrl $_.Url $_.Title })
}
finally {
    $favoritesConnection.Dispose()
}

$candidateMap = @{}
foreach ($item in $historyItems) {
    $candidateMap[$item.Url] = [pscustomobject]@{
        Url = $item.Url
        Title = $item.Title
        LastVisitedAt = $item.LastVisitedAt
        VisitCount = $item.VisitCount
        IsFavorite = $false
        IsHistory = $true
    }
}

foreach ($favorite in $favoriteItems) {
    if ($candidateMap.ContainsKey($favorite.Url)) {
        $candidateMap[$favorite.Url].Title = if ([string]::IsNullOrWhiteSpace($favorite.Title)) { $candidateMap[$favorite.Url].Title } else { $favorite.Title }
        $candidateMap[$favorite.Url].IsFavorite = $true
        if ($favorite.UpdatedAt -gt $candidateMap[$favorite.Url].LastVisitedAt) {
            $candidateMap[$favorite.Url].LastVisitedAt = $favorite.UpdatedAt
        }
    }
    else {
        $candidateMap[$favorite.Url] = [pscustomobject]@{
            Url = $favorite.Url
            Title = $favorite.Title
            LastVisitedAt = $favorite.UpdatedAt
            VisitCount = 0
            IsFavorite = $true
            IsHistory = $false
        }
    }
}

$candidates = @($candidateMap.Values)

$selectedByCategory = @{}
foreach ($category in $categories) {
    $selectedByCategory[$category.Name] = @(
        $candidates |
            Where-Object { Test-PatternMatch $_ $category.Patterns } |
            Sort-Object @{ Expression = { Get-CandidateScore $_ }; Descending = $true }, @{ Expression = "LastVisitedAt"; Descending = $true } |
            Select-Object -First $ItemsPerCollection
    )
}

Write-Host "LinkScape Smart Collections preview"
Write-Host "Cache: $cachePath"
Write-Host "History rows scanned: $($historyItems.Count)"
Write-Host "Favorites rows scanned: $($favoriteItems.Count)"
Write-Host "Algorithm: merge local History and Favorites by URL, remove sign-in/auth pages, match topic by domain/URL, then keep the top $ItemsPerCollection ranked items per collection."
Write-Host ""

foreach ($category in $categories) {
    $items = $selectedByCategory[$category.Name]
    Write-Host "$($category.Name): $($items.Count) item(s)"
    foreach ($item in ($items | Select-Object -First $ItemsPerCollection)) {
        Write-Host "  - $($item.Title) <$($item.Url)>"
    }
}

if (-not $PSCmdlet.ShouldProcess($collectionsDbPath, "Create or update LinkScape Smart Collections")) {
    return
}

$collectionConnection = New-SqliteConnection $collectionsDbPath
try {
    Initialize-CollectionsDatabase $collectionConnection

    foreach ($category in $categories) {
        $items = $selectedByCategory[$category.Name]
        $existingCollectionId = Invoke-Scalar $collectionConnection `
            "SELECT Id FROM TabCollections WHERE Name = `$name COLLATE NOCASE LIMIT 1;" `
            @{ '$name' = $category.Name }

        if ($items.Count -eq 0) {
            if ($null -ne $existingCollectionId -and -not [Convert]::IsDBNull($existingCollectionId)) {
                Invoke-NonQuery $collectionConnection `
                    "DELETE FROM TabCollectionItems WHERE CollectionId = `$collectionId;" `
                    @{ '$collectionId' = [string]$existingCollectionId }
            }
            continue
        }

        $collectionId = Get-OrCreateCollection $collectionConnection $category.Name
        Invoke-NonQuery $collectionConnection `
            "DELETE FROM TabCollectionItems WHERE CollectionId = `$collectionId;" `
            @{ '$collectionId' = $collectionId }
        foreach ($item in $items) {
            Add-CollectionItem $collectionConnection $collectionId $item
        }
    }
}
finally {
    $collectionConnection.Dispose()
}

Write-Host ""
Write-Host "Done. Open LinkScape collections to see the generated Smart Collections."
