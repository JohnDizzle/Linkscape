$ErrorActionPreference = 'Stop'

$ffmpeg = 'C:\Users\fizzl\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1.2-full_build\bin\ffmpeg.exe'
$outDir = 'C:\Users\fizzl\source\repos\JohnDizzle\AI-Agent\storefront-promo'
$font = 'C\:/Windows/Fonts/segoeui.ttf'
$boldFont = 'C\:/Windows/Fonts/segoeuib.ttf'

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Invoke-FFmpeg {
    param([string[]]$FfmpegArgs)
    & $ffmpeg @FfmpegArgs
    if ($LASTEXITCODE -ne 0) {
        throw "FFmpeg failed with exit code $LASTEXITCODE"
    }
}

function Render-Title {
    param(
        [string]$Output,
        [int]$Duration,
        [string]$Title = 'LinkScape Browser',
        [string]$Subtitle = 'Save sessions. Restore context. Ask Linker.',
        [string]$Body = 'A focused browser workspace for Windows.'
    )
    $vf = "drawbox=x=0:y=0:w=iw:h=ih:color=0x101418@1:t=fill," +
        "drawbox=x=0:y=0:w=iw:h=ih:color=0x0f766e@0.20:t=fill," +
        "drawtext=fontfile='$boldFont':text='$Title':x=120:y=300:fontsize=84:fontcolor=white," +
        "drawtext=fontfile='$font':text='$Subtitle':x=124:y=410:fontsize=38:fontcolor=0xd8f3ef," +
        "drawtext=fontfile='$font':text='$Body':x=124:y=480:fontsize=30:fontcolor=0xb7c8c4"

    Invoke-FFmpeg @(
        '-y',
        '-f', 'lavfi', '-i', "color=c=0x101418:s=1920x1080:d=${Duration}:r=30",
        '-f', 'lavfi', '-i', "anullsrc=channel_layout=stereo:sample_rate=48000",
        '-t', "$Duration",
        '-vf', $vf,
        '-map', '0:v:0', '-map', '1:a:0',
        '-c:v', 'libx264', '-preset', 'medium', '-crf', '18',
        '-c:a', 'aac', '-b:a', '128k',
        '-pix_fmt', 'yuv420p', '-r', '30', '-shortest',
        $Output
    )
}

function Render-VideoSegment {
    param(
        [string]$Source,
        [string]$Output,
        [string]$Caption,
        [string]$Start,
        [int]$Duration
    )
    $vf = "scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080," +
        "drawbox=x=0:y=880:w=iw:h=200:color=0x101418@0.62:t=fill," +
        "drawtext=fontfile='$boldFont':text='$Caption':x=96:y=925:fontsize=46:fontcolor=white"

    Invoke-FFmpeg @(
        '-y',
        '-ss', $Start,
        '-t', "$Duration",
        '-i', $Source,
        '-f', 'lavfi', '-t', "$Duration", '-i', "anullsrc=channel_layout=stereo:sample_rate=48000",
        '-vf', $vf,
        '-map', '0:v:0', '-map', '1:a:0',
        '-c:v', 'libx264', '-preset', 'medium', '-crf', '19',
        '-c:a', 'aac', '-b:a', '128k',
        '-pix_fmt', 'yuv420p', '-r', '30', '-shortest',
        $Output
    )
}

function Render-ImageSegment {
    param(
        [string]$Source,
        [string]$Output,
        [string]$Caption,
        [string]$Subcaption,
        [int]$Duration
    )
    $vf = "[0:v]scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080,boxblur=24:2,eq=brightness=-0.14:saturation=0.95[bg];" +
        "[0:v]scale=1780:900:force_original_aspect_ratio=decrease[fg];" +
        "[bg][fg]overlay=(W-w)/2:70,drawbox=x=0:y=850:w=iw:h=230:color=0x101418@0.70:t=fill," +
        "drawtext=fontfile='$boldFont':text='$Caption':x=96:y=895:fontsize=48:fontcolor=white," +
        "drawtext=fontfile='$font':text='$Subcaption':x=100:y=960:fontsize=30:fontcolor=0xd8f3ef[v]"

    Invoke-FFmpeg @(
        '-y',
        '-loop', '1', '-t', "$Duration", '-i', $Source,
        '-f', 'lavfi', '-t', "$Duration", '-i', "anullsrc=channel_layout=stereo:sample_rate=48000",
        '-filter_complex', $vf,
        '-map', '[v]', '-map', '1:a:0',
        '-c:v', 'libx264', '-preset', 'medium', '-crf', '18',
        '-c:a', 'aac', '-b:a', '128k',
        '-pix_fmt', 'yuv420p', '-r', '30', '-shortest',
        $Output
    )
}

$segments = @(
    (Join-Path $outDir 'segment_01_loading.mp4'),
    (Join-Path $outDir 'segment_02_welcome.mp4'),
    (Join-Path $outDir 'segment_03_vertical_tabs.mp4'),
    (Join-Path $outDir 'segment_04_import_menu.mp4'),
    (Join-Path $outDir 'segment_05_import_result.mp4'),
    (Join-Path $outDir 'segment_06_history_import.mp4'),
    (Join-Path $outDir 'segment_07_collections.mp4'),
    (Join-Path $outDir 'segment_08_linker_mcp.mp4'),
    (Join-Path $outDir 'segment_09_api_key.mp4'),
    (Join-Path $outDir 'segment_10_themes.mp4'),
    (Join-Path $outDir 'segment_11_store_proof.mp4'),
    (Join-Path $outDir 'segment_12_close.mp4')
)

Render-ImageSegment -Source 'C:\Users\fizzl\source\repos\JohnDizzle\AI-Agent\photos\AppSplashScreen (Large).png' -Output $segments[0] -Caption 'Loading state prepares your browser workspace' -Subcaption 'Tabs, favorites, and history get ready before the shell opens.' -Duration 5
Render-ImageSegment -Source 'C:\Users\fizzl\source\repos\JohnDizzle\AI-Agent\photos\AppBackDefaultBackDrops..png' -Output $segments[1] -Caption 'Welcome to a Microsoft-native browser workspace' -Subcaption 'A clean Windows UI with vertical tabs, backdrop controls, and quick commands.' -Duration 5
Render-ImageSegment -Source 'C:\Users\fizzl\source\repos\JohnDizzle\AI-Agent\photos\AppHomeTabs.png' -Output $segments[2] -Caption 'Open vertical tabs keep context visible' -Subcaption 'See the active tab, session time, visits, and controls without leaving the page.' -Duration 5
Render-ImageSegment -Source 'C:\Users\fizzl\source\repos\JohnDizzle\AI-Agent\photos\RailImportFavortieProfiles.png' -Output $segments[3] -Caption 'Import favorites by browser and profile' -Subcaption 'Bring bookmarks from Edge, Brave, Vivaldi, and profile-specific sources.' -Duration 5
Render-ImageSegment -Source 'C:\Users\fizzl\source\repos\JohnDizzle\AI-Agent\photos\RailImportFavortieProfiles2.png' -Output $segments[4] -Caption 'Imported favorites land directly in LinkScape' -Subcaption 'Profiles sync into the rail so saved destinations are ready to open.' -Duration 5
Render-ImageSegment -Source 'C:\Users\fizzl\source\repos\JohnDizzle\AI-Agent\photos\RailImportHistory.png' -Output $segments[5] -Caption 'History imports preserve grouped browsing context' -Subcaption 'Timeline items stay searchable and organized across profiles and browsers.' -Duration 5
Render-ImageSegment -Source 'C:\Users\fizzl\AppData\Local\Temp\codex-clipboard-fa5b81e2-0a06-4f0e-bdac-292e3a787efe.png' -Output $segments[6] -Caption 'Collections reopen the right workspace on launch' -Subcaption 'Set startup collections and restore the tabs you actually use.' -Duration 5
Render-ImageSegment -Source 'C:\Users\fizzl\AppData\Local\Temp\codex-clipboard-4fc1b473-61bb-411c-a76c-087a975e4a28.png' -Output $segments[7] -Caption 'Linker adds MCP tooling through a chat interface' -Subcaption 'Ask about tabs, collections, favorites, status, browser data, and local tools.' -Duration 5
Render-ImageSegment -Source 'C:\Users\fizzl\Pictures\Screenshots\Screenshot 2026-07-20 032150.png' -Output $segments[8] -Caption 'Connect an API key for provider-backed answers' -Subcaption 'Choose the provider, save securely, and let Linker power richer workflows.' -Duration 5
Render-VideoSegment -Source 'C:\Users\fizzl\Downloads\LinkScapeStoreThemes.mp4' -Output $segments[9] -Caption 'The newest Microsoft Reactor UI styling meets MCP-powered tooling' -Start '00:00:02' -Duration 7
Render-ImageSegment -Source 'C:\Users\fizzl\Pictures\Screenshots\Screenshot 2026-07-24 013506.png' -Output $segments[10] -Caption 'Built for Store users who live in the browser' -Subcaption 'Partner Center signals real interest, strong reliability, and room to grow.' -Duration 5
Render-Title -Output $segments[11] -Duration 5 -Title 'LinkScape Browser' -Subtitle 'Microsoft Reactor UI plus MCP chat tooling' -Body 'A smarter Windows storefront experience for tabs, history, imports, and collections.'

$concatFile = Join-Path $outDir 'concat_list.txt'
$concatLines = $segments | ForEach-Object { "file '$($_ -replace "'", "'\''")'" }
Set-Content -LiteralPath $concatFile -Value $concatLines -Encoding ASCII

$final = Join-Path $outDir 'LinkScape_MS_Store_Promo.mp4'
Invoke-FFmpeg @(
    '-y',
    '-f', 'concat', '-safe', '0', '-i', $concatFile,
    '-c', 'copy',
    $final
)

Write-Output $final
