<#
.SYNOPSIS
    Removes the Wings Test Orchestrator site and app pool from IIS.

.DESCRIPTION
    The data folder (evidence, packages, encryption keys) and the database are kept unless -RemoveData is given,
    and the database is never dropped by this script.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]$SiteName = 'WingsTestOrchestrator',
    [string]$InstallPath = 'C:\inetpub\WingsTestOrchestrator',
    [string]$DataPath = 'C:\ProgramData\WingsTestOrchestrator',
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

if ($PSCmdlet.ShouldProcess($SiteName, 'Remove site, app pool and installed files')) {
    if (Test-Path "IIS:\Sites\$SiteName") { Remove-Website -Name $SiteName }
    if (Test-Path "IIS:\AppPools\$SiteName") {
        if ((Get-WebAppPoolState -Name $SiteName).Value -ne 'Stopped') { Stop-WebAppPool -Name $SiteName }
        for ($i = 0; $i -lt 30 -and (Get-WebAppPoolState -Name $SiteName).Value -ne 'Stopped'; $i++) { Start-Sleep -Seconds 1 }
        Remove-WebAppPool -Name $SiteName
    }
    if (Test-Path $InstallPath) { Remove-Item $InstallPath -Recurse -Force }
    Get-NetFirewallRule -DisplayName 'Wings Test Orchestrator (*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
}

if ($RemoveData -and $PSCmdlet.ShouldProcess($DataPath, 'Permanently delete evidence, packages and encryption keys')) {
    if (Test-Path $DataPath) { Remove-Item $DataPath -Recurse -Force }
}

Write-Host "Removed $SiteName. The database was not touched$(if (-not $RemoveData) { "; $DataPath was kept" })."
