#requires -RunAsAdministrator
<#
Installs or uninstalls Arbor.Symbols.Server as a Windows Service.
Running as a service is opt-in: the app itself behaves as a normal console
app unless it's actually launched by the Service Control Manager (see
builder.Host.UseWindowsService() in Program.cs). This script just registers
that launch with Windows.

Usage:
  scripts\windows-service.ps1 -Install -ExePath C:\path\to\Arbor.Symbols.Server.exe
  scripts\windows-service.ps1 -Uninstall

The exe at -ExePath must be a framework-dependent, Windows-targeted publish,
e.g.:
  dotnet publish src\Arbor.Symbols.Server\Arbor.Symbols.Server.csproj `
    --configuration Release --runtime win-x64 --self-contained false
#>
param(
    [switch]$Install,
    [switch]$Uninstall,
    [string]$ExePath,
    [string]$ServiceName = "Arbor.Symbols.Server",
    [string]$DisplayName = "Arbor.Symbols Server",
    [string]$Description = "Symbol server (disk cache / MS symbol server / ILSpy PDB generation).",
    [ValidateSet("auto", "demand", "disabled")]
    [string]$StartupType = "auto"
)

$ErrorActionPreference = "Stop"

if (-not ($Install -xor $Uninstall)) {
    Write-Error "Specify exactly one of -Install or -Uninstall."
    exit 1
}

if ($Uninstall) {
    Write-Host "Stopping and removing service '$ServiceName'..."
    sc.exe stop $ServiceName | Out-Null
    sc.exe delete $ServiceName
    exit 0
}

if (-not $ExePath) {
    Write-Error "-ExePath is required with -Install."
    exit 1
}
$ExePath = (Resolve-Path $ExePath).Path
if (-not (Test-Path $ExePath)) {
    Write-Error "File not found: $ExePath"
    exit 1
}

Write-Host "Creating service '$ServiceName' -> $ExePath"
sc.exe create $ServiceName binPath= "`"$ExePath`"" start= $StartupType DisplayName= "`"$DisplayName`""
sc.exe description $ServiceName "`"$Description`""

Write-Host "Starting service '$ServiceName'..."
sc.exe start $ServiceName
