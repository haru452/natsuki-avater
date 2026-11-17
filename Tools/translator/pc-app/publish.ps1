# Builds publish/translator_pc.exe (one file, no .NET install needed) and puts a
# "VRC Translator PC" shortcut on the desktop. Run: powershell -ExecutionPolicy Bypass -File publish.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $here 'publish'

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe' }

& $dotnet publish (Join-Path $here 'TranslatorPc\TranslatorPc.csproj') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$exe = Join-Path $out 'translator_pc.exe'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'VRC Translator PC.lnk'))
$link.TargetPath = $exe
$link.WorkingDirectory = $out
$link.Description = 'VRC Translator: PC side (OSC relay + microphone + VRChat sound)'
$link.Save()
Write-Host "Done: $exe"
Write-Host "Desktop shortcut: VRC Translator PC"
