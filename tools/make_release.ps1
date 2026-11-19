<#
Makes a release: one folder, zipped, that runs on another computer (tools\release\README.txt says what is in it).

  tools\make_release.cmd [-Build] [-WithBand] [-WithSongs] [-WithSettings] [-Out <folder>]

The program only: the game and the engine as last built (-Build builds both first: game\build_desktop.cmd,
engine\build.cmd), with songtool beside the engine, published self-contained (the control panel's Import... runs it;
no .NET needed on the other computer), and the Visual C++ runtime the engine uses (msvcp140, vcruntime140,
vcruntime140_1), so nothing needs installing; the plugins are installed on their own. The demo songs
(tools\demosongs\songs) are always in: imported into the release's songs-local, numbered so that the game opens
Highway Boogie first (1-highway-boogie-gp5.song.json, ...), their files in songs-local\gpx. -WithSongs adds the songs
(songs-local), -WithSettings the band's
sounds (%APPDATA%\YourBand: band-defaults.sounds, songs\*.sounds); the engine's own settings never go: they name the plugins by where they are on this computer. Written to
Documents\YourBand-releases\YourBand-<date>-<commit>.zip (and the folder it zips).

The 3D band is an optional download, the band pack (game\Builds\BandPack\Band\band.bundle, made by the game's
build): by default it goes in a zip of its own, YourBand-band-pack-<date>-<commit>.zip, to unzip into the release's
folder (the one with Game and Engine; it holds Game\Band\band.bundle and a NOTICE.txt with the licences of what is inside). -WithBand puts it in
the release's own zip instead.
#>
param(
    [switch]$Build,
    [switch]$WithBand,
    [switch]$WithSongs,
    [switch]$WithSettings,
    [string]$Out = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'YourBand-releases')
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$game = Join-Path $repo 'game\Builds\Desktop'
$engine = Join-Path $repo 'engine\build\Release\gt_engine_artefacts\Release\YourBand Engine.exe'
$pack = Join-Path $repo 'game\Builds\BandPack\Band\band.bundle'

if ($Build) {
    & (Join-Path $repo 'engine\build.cmd')
    if ($LASTEXITCODE -ne 0) { throw 'The engine did not build.' }
    & (Join-Path $repo 'game\build_desktop.cmd')
    if ($LASTEXITCODE -ne 0) { throw 'The game did not build.' }
}
foreach ($needed in (Join-Path $game 'YourBand.exe'), $engine) {
    if (-not (Test-Path $needed)) { throw "Not built: $needed (build it, or run with -Build)" }
}

# the release's name: the date and the commit (with "+" when the working tree has changes not committed)
$commit = (git -C $repo rev-parse --short HEAD).Trim()
$changed = git -C $repo status --porcelain --untracked-files=no
$name = 'YourBand-{0}-{1}{2}' -f (Get-Date -Format 'yyyy-MM-dd'), $commit, $(if ($changed) { '+' } else { '' })
$folder = Join-Path $Out $name
if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
New-Item -ItemType Directory -Force $folder | Out-Null

function Copy-Tree($from, $to, [string[]]$excludeDirs = @()) {
    # robocopy: exit codes below 8 are success
    $arguments = @($from, $to, '/E', '/NJH', '/NJS', '/NFL', '/NDL', '/NP')
    if ($excludeDirs) { $arguments += '/XD'; $arguments += $excludeDirs }
    robocopy @arguments | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying $from failed (robocopy $LASTEXITCODE)" }
}

Write-Host "Game..."
# (the game's own Band folder is a pack copied there to try it: the pack is a download of its own)
Copy-Tree $game (Join-Path $folder 'Game') @('YourBand_BackUpThisFolder_ButDontShipItWithYourGame', (Join-Path $game 'Band'))

# the band pack's folder (Game\Band: band.bundle and the licences of what is in it), in a release or in a zip of its own
function Write-BandPack($gameFolder) {
    $band = Join-Path $gameFolder 'Band'
    New-Item -ItemType Directory -Force $band | Out-Null
    Copy-Item $pack $band
    $notice = (Get-Content (Join-Path $PSScriptRoot 'release\band-pack-NOTICE.txt') -Raw) + "`r`n" +
        (Get-Content (Join-Path $repo 'docs\licenses\Microsoft-Rocketbox-LICENSE.txt') -Raw)
    Set-Content (Join-Path $band 'NOTICE.txt') $notice -Encoding UTF8
}
if ($WithBand) {
    if (-not (Test-Path $pack)) { throw "No band pack: $pack (the game's build makes it from game\Assets\External; docs\band-assets.md)" }
    Write-Host "Band pack (in the release)..."
    Write-BandPack (Join-Path $folder 'Game')
}

Write-Host "Engine..."
$engineDir = Join-Path $folder 'Engine'
New-Item -ItemType Directory -Force $engineDir | Out-Null
Copy-Item $engine $engineDir
foreach ($dll in 'msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll') {
    Copy-Item (Join-Path $env:WINDIR "System32\$dll") $engineDir
}

Write-Host "songtool..."
dotnet publish (Join-Path $repo 'tools\songtool') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $engineDir 'songtool') --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'songtool did not publish.' }
Get-ChildItem (Join-Path $engineDir 'songtool') -Include *.pdb, *.xml -Recurse | Remove-Item  # (debug symbols, a library's API docs)

# the demo songs, imported by the release's own songtool, so they play from the start; numbered in this order (the
# game opens the first song by file name, and the engine's song list shows the names)
Write-Host "Demo songs..."
$songs = Join-Path $folder 'songs-local'
$demos = Join-Path $songs 'gpx'
New-Item -ItemType Directory -Force $demos | Out-Null
$number = 0
foreach ($title in 'Highway Boogie', 'Basement Show', 'Lantern Light', 'Paper Sky') {
    $demo = Join-Path $PSScriptRoot "demosongs\songs\YourBand Demo - $title.gp5"
    Copy-Item $demo $demos
    $saved = & (Join-Path $engineDir 'songtool\songtool.exe') import --gp (Join-Path $demos (Split-Path $demo -Leaf)) --out-dir $songs |
        Select-String '^Saved (.+)$' | ForEach-Object { $_.Matches[0].Groups[1].Value }
    if ($LASTEXITCODE -ne 0 -or -not $saved) { throw "The demo song $title did not import." }
    $number++
    Rename-Item $saved ('{0}-{1}' -f $number, (Split-Path $saved -Leaf))
}

if ($WithSongs) {
    Write-Host "Songs..."
    Copy-Tree (Join-Path $repo 'songs-local') $songs
}

if ($WithSettings) {
    Write-Host "Settings..."
    $settings = Join-Path $folder 'settings'
    New-Item -ItemType Directory -Force (Join-Path $settings 'songs') | Out-Null
    $appData = Join-Path $env:APPDATA 'YourBand'
    if (Test-Path (Join-Path $appData 'band-defaults.sounds')) { Copy-Item (Join-Path $appData 'band-defaults.sounds') $settings }
    Get-ChildItem (Join-Path $appData 'songs') -Filter *.sounds -ErrorAction SilentlyContinue | Copy-Item -Destination (Join-Path $settings 'songs')
    Copy-Item (Join-Path $PSScriptRoot 'release\copy-settings.cmd') $settings
}

Copy-Item (Join-Path $PSScriptRoot 'release\README.txt') $folder
Set-Content (Join-Path $folder 'VERSION.txt') "$name`r`nmade $(Get-Date -Format 'yyyy-MM-dd HH:mm') from commit $(git -C $repo rev-parse HEAD)$(if ($changed) { ' with changes not committed' })" -Encoding UTF8

Write-Host "Zip..."
$zip = "$folder.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
tar.exe -a -c -f $zip -C $Out $name  # (Windows' own tar writes a zip by its extension; faster than Compress-Archive)
if ($LASTEXITCODE -ne 0) { throw 'The zip failed.' }
Write-Host ("Made {0} ({1:N0} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

if (-not $WithBand -and (Test-Path $pack)) {
    Write-Host "Band pack zip..."
    $packName = $name -replace '^YourBand-', 'YourBand-band-pack-'
    $stage = Join-Path $Out "$packName-stage"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    Write-BandPack (Join-Path $stage 'Game')
    $packZip = Join-Path $Out "$packName.zip"
    if (Test-Path $packZip) { Remove-Item $packZip -Force }
    tar.exe -a -c -f $packZip -C $stage Game
    if ($LASTEXITCODE -ne 0) { throw 'The band pack zip failed.' }
    Remove-Item $stage -Recurse -Force
    Write-Host ("Made {0} ({1:N0} MB)" -f $packZip, ((Get-Item $packZip).Length / 1MB))
} elseif (-not $WithBand) {
    Write-Host "No band pack built (game\Builds\BandPack): the release has no 3D band."
}
