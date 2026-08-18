# Builds pc-tool's CLI and GUI as self-contained single-file .exe files for
# Windows and copies them into output/. Run on Windows (see build.sh for
# Linux/Mac). Written for Windows PowerShell 5.1 compatibility (the default
# shell on a stock Windows install).

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PcToolDir = Join-Path $ScriptDir "pc-tool"
$OutputDir = Join-Path $ScriptDir "output"

$Rid = "win-x64"
if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") {
    $Rid = "win-arm64"
}

Write-Host "== Building for $Rid =="

if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Path $OutputDir | Out-Null

function Build-Project {
    param(
        [string]$Project,
        [string]$Name
    )

    $TmpDir = Join-Path $env:TEMP ([System.Guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $TmpDir | Out-Null

    Write-Host "-- $Name ($Project) --"
    dotnet publish $Project `
        -c Release `
        -r $Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $TmpDir

    Copy-Item (Join-Path $TmpDir "$Name.exe") (Join-Path $OutputDir "$Name.exe") -Force
    Remove-Item -Recurse -Force $TmpDir
}

Build-Project (Join-Path $PcToolDir "SwitchCloudSaveBrew.Cli\SwitchCloudSaveBrew.Cli.csproj") "SwitchCloudSaveBrew.Cli"
Build-Project (Join-Path $PcToolDir "SwitchCloudSaveBrew.Gui\SwitchCloudSaveBrew.Gui.csproj") "SwitchCloudSaveBrew.Gui"

Write-Host "== Done: $OutputDir =="
Get-ChildItem $OutputDir
