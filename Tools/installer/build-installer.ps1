# Builds the VRoom Windows player and packs it into Builds/Installer/VRoom-Setup.exe on this computer.
# Usage (PowerShell, from the repo root):  Tools/installer/build-installer.ps1 -Version 0.1.5
# Close the Unity editor first: a project can only be open in one Unity at a time.
param(
    [string]$Version = "0.1.0",
    [string]$Unity = "",
    [switch]$SkipUnity
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $root

if (-not $SkipUnity) {
    if (-not $Unity) {
        $editorVersion = (Get-Content ProjectSettings/ProjectVersion.txt | Select-Object -First 1) -replace "m_EditorVersion:\s*", ""
        $Unity = "C:\Program Files\Unity\Hub\Editor\$editorVersion\Editor\Unity.exe"
    }
    if (-not (Test-Path $Unity)) { throw "Unity not found at $Unity. Pass -Unity <path to Unity.exe>." }

    $env:VROOM_VERSION = $Version
    & $Unity -batchmode -quit -projectPath $root -executeMethod VRoom.EditorTools.BuildWindows.Run -logFile Builds/unity-build.log
    if ($LASTEXITCODE -ne 0) { throw "The Unity build failed, see Builds/unity-build.log" }
}

if (-not (Test-Path "Builds/Windows/VRoom.exe")) { throw "Builds/Windows/VRoom.exe is missing." }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 is not installed. Install it with: winget install JRSoftware.InnoSetup" }

& $iscc "/DAppVersion=$Version" "Tools/installer/VRoom.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

Compress-Archive -Path "Builds/Windows/*" -DestinationPath "Builds/Installer/VRoom-Windows.zip" -Force
Write-Host "Done: Builds/Installer/VRoom-Setup.exe (version $Version)"
