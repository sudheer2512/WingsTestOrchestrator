<#
.SYNOPSIS
    Installs or upgrades Wings Test Orchestrator in IIS. Run from the extracted package in an elevated PowerShell.

.DESCRIPTION
    First install: creates the app pool and site, the data folder, the database (local SQL Server) and
    appsettings.Production.json with a new signing key and first administrator.
    Upgrade: run the newer package's script with the same -SiteName. Settings, data and database are kept;
    the previous site files are kept in <DataPath>\previous-site.

.EXAMPLE
    .\Install-Wings.ps1 -Port 8080 -OpenFirewall

.EXAMPLE
    .\Install-Wings.ps1 -Port 443 -HostName wto.wings.local -CertificateThumbprint ABC123... -SqlServer SQL01\QA

.EXAMPLE
    .\Install-Wings.ps1 -DatabaseProvider Sqlite -SeedSampleData     # evaluation without SQL Server
#>
[CmdletBinding()]
param(
    [string]$SiteName = 'WingsTestOrchestrator',
    [string]$InstallPath = 'C:\inetpub\WingsTestOrchestrator',
    # Holds evidence, packages and encryption keys. Back this up together with the database.
    [string]$DataPath = 'C:\ProgramData\WingsTestOrchestrator',
    [int]$Port = 8080,
    [string]$HostName = '',
    # Certificate in LocalMachine\My. With it, $Port is served over HTTPS.
    [string]$CertificateThumbprint = '',
    # Address people and agents use; goes into email, Teams and tracker links.
    [string]$PublicUrl = '',
    [ValidateSet('SqlServer', 'Sqlite')]
    [string]$DatabaseProvider = 'SqlServer',
    [string]$SqlServer = '.',
    [string]$DatabaseName = 'WingsTestOrchestrator',
    # Full connection string, for SQL logins or a remote server. Create the database and its login yourself.
    [string]$ConnectionString = '',
    [string]$AdminUserName = 'admin',
    [switch]$SeedSampleData,
    [switch]$OpenFirewall,
    [switch]$SkipFeatureInstall
)

$ErrorActionPreference = 'Stop'
$package = Join-Path $PSScriptRoot 'site'
$pool = $SiteName
$poolAccount = "IIS AppPool\$pool"
$settingsPath = Join-Path $InstallPath 'appsettings.Production.json'
$adminsSid = '*S-1-5-32-544'
$systemSid = '*S-1-5-18'

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

function Invoke-Icacls {
    & icacls.exe @args | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls failed: $($args -join ' ')" }
}

function New-Secret([int]$bytes) {
    $buffer = New-Object byte[] $bytes
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($buffer)
    $rng.Dispose()
    return [Convert]::ToBase64String($buffer)
}

function New-AdminPassword {
    # Letters and digits only; the Aa1 prefix satisfies the upper, lower and digit rules for any random part.
    return 'Aa1' + ((New-Secret 24) -replace '[^A-Za-z0-9]', '').Substring(0, 21)
}

function Write-Settings($settings) {
    $json = $settings | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($settingsPath, $json, (New-Object Text.UTF8Encoding $false))
}

function Invoke-Sql($connection, $text) {
    $command = $connection.CreateCommand()
    $command.CommandText = $text
    $command.CommandTimeout = 300
    [void]$command.ExecuteNonQuery()
}

function Initialize-SqlDatabase {
    # Windows sign-in of the person running this script: needs sysadmin, or dbcreator plus securityadmin.
    Step "Preparing database $DatabaseName on $SqlServer"
    $connection = New-Object Data.SqlClient.SqlConnection "Server=$SqlServer;Database=master;Integrated Security=True;TrustServerCertificate=True"
    $connection.Open()
    try {
        $db = '[' + $DatabaseName.Replace(']', ']]') + ']'
        $login = '[' + $poolAccount.Replace(']', ']]') + ']'
        $dbText = $DatabaseName.Replace("'", "''")
        $loginText = $poolAccount.Replace("'", "''")
        Invoke-Sql $connection "IF DB_ID(N'$dbText') IS NULL CREATE DATABASE $db"
        Invoke-Sql $connection "IF SUSER_ID(N'$loginText') IS NULL CREATE LOGIN $login FROM WINDOWS"
        Invoke-Sql $connection "USE $db; IF USER_ID(N'$loginText') IS NULL CREATE USER $login FOR LOGIN $login; ALTER ROLE db_owner ADD MEMBER $login"
    }
    finally { $connection.Dispose() }
}

function Wait-Healthy {
    $scheme = if ($CertificateThumbprint) { 'https' } else { 'http' }
    $hostHeader = if ($HostName) { $HostName } else { 'localhost' }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    # This probe goes to 127.0.0.1, so the certificate's name cannot match. Nothing is sent but the health request.
    [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
    try {
        for ($i = 0; $i -lt 60; $i++) {
            try {
                $response = Invoke-WebRequest "${scheme}://127.0.0.1:$Port/health" -Headers @{ Host = $hostHeader } -UseBasicParsing -TimeoutSec 30
                if ($response.StatusCode -eq 200) { return $true }
            }
            catch { Start-Sleep -Seconds 3 }
        }
        return $false
    }
    finally { [Net.ServicePointManager]::ServerCertificateValidationCallback = $null }
}

# ---- checks ----------------------------------------------------------------------------------------------------

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script in an elevated (Run as administrator) PowerShell.' }
if (-not (Test-Path (Join-Path $package 'Wings.Api.dll'))) { throw "The published site was not found in $package. Run the script from the extracted package." }
if ($CertificateThumbprint -and -not (Test-Path "Cert:\LocalMachine\My\$CertificateThumbprint")) { throw "Certificate $CertificateThumbprint is not in LocalMachine\My." }

# Files from a downloaded zip carry a web mark that can stop them loading.
Get-ChildItem $PSScriptRoot -Recurse -File | Unblock-File

# ---- IIS features ----------------------------------------------------------------------------------------------

if (-not $SkipFeatureInstall) {
    Step 'Enabling IIS features (web server, WebSockets, application initialization)'
    if (Get-Command Install-WindowsFeature -ErrorAction SilentlyContinue) {
        [void](Install-WindowsFeature Web-Server, Web-WebSockets, Web-AppInit, Web-Mgmt-Console, Web-Scripting-Tools)
    }
    else {
        [void](Enable-WindowsOptionalFeature -Online -All -NoRestart -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-WebSockets, IIS-ApplicationInit, IIS-ManagementConsole, IIS-ManagementScriptingTools)
    }
}

$module = Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
$runtimes = if (Get-Command dotnet -ErrorAction SilentlyContinue) { (& dotnet --list-runtimes) -join "`n" } else { '' }
if (-not (Test-Path $module) -or $runtimes -notmatch 'Microsoft\.AspNetCore\.App 10\.') {
    throw @'
The ASP.NET Core 10 Hosting Bundle is missing, or was installed before IIS.
Install (or repair) it from https://dotnet.microsoft.com/download/dotnet/10.0 (Hosting Bundle under ASP.NET Core Runtime),
run "iisreset", then run this script again.
'@
}

Import-Module WebAdministration
$upgrade = Test-Path $settingsPath

# ---- files -------------------------------------------------------------------------------------------------------

if (Test-Path "IIS:\AppPools\$pool") {
    if ((Get-WebAppPoolState -Name $pool).Value -ne 'Stopped') {
        Step 'Stopping the running site'
        Stop-WebAppPool -Name $pool
        for ($i = 0; $i -lt 30 -and (Get-WebAppPoolState -Name $pool).Value -ne 'Stopped'; $i++) { Start-Sleep -Seconds 1 }
    }
}

foreach ($folder in @($InstallPath, (Join-Path $InstallPath 'logs'), $DataPath, (Join-Path $DataPath 'storage'))) {
    [void](New-Item -ItemType Directory -Force -Path $folder)
}

if ($upgrade) {
    Step "Keeping the current site in $DataPath\previous-site"
    & robocopy.exe $InstallPath (Join-Path $DataPath 'previous-site') /MIR /XF appsettings.Production.json /XD logs /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Backing up the site failed (robocopy exit code $LASTEXITCODE)." }
}

Step "Copying the site to $InstallPath"
& robocopy.exe $package $InstallPath /MIR /XF appsettings.Production.json /XD logs /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying the site failed (robocopy exit code $LASTEXITCODE)." }

# ---- app pool (stopped until settings and permissions are in place) ---------------------------------------------

Step "Configuring app pool $pool"
if (-not (Test-Path "IIS:\AppPools\$pool")) { [void](New-WebAppPool -Name $pool) }
$poolPath = "IIS:\AppPools\$pool"
Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ''
Set-ItemProperty $poolPath -Name enable32BitAppOnWin64 -Value $false
Set-ItemProperty $poolPath -Name processModel.identityType -Value ApplicationPoolIdentity
Set-ItemProperty $poolPath -Name processModel.loadUserProfile -Value $true
# The job queue, scheduler, lease monitor and notification monitor live in this process: it must keep running.
Set-ItemProperty $poolPath -Name startMode -Value AlwaysRunning
Set-ItemProperty $poolPath -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty $poolPath -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
if ((Get-WebAppPoolState -Name $pool).Value -ne 'Stopped') { Stop-WebAppPool -Name $pool }

# ---- database (needs the app pool's identity; before the settings, so a failed attempt can simply be run again) ------

if (-not $upgrade -and $DatabaseProvider -eq 'SqlServer' -and -not $ConnectionString) { Initialize-SqlDatabase }

# ---- settings ----------------------------------------------------------------------------------------------------

$adminPassword = $null
if ($upgrade) {
    Step 'Keeping the existing appsettings.Production.json'
}
else {
    Step 'Creating appsettings.Production.json'
    if ($DatabaseProvider -eq 'Sqlite') { $connection = "Data Source=$DataPath\wings.db;Default Timeout=30" }
    elseif ($ConnectionString) { $connection = $ConnectionString }
    else { $connection = "Server=$SqlServer;Database=$DatabaseName;Trusted_Connection=True;TrustServerCertificate=True" }
    $adminPassword = New-AdminPassword
    $settings = [ordered]@{
        Database          = [ordered]@{ Provider = $DatabaseProvider; MigrateOnStartup = $true }
        ConnectionStrings = [ordered]@{ Wings = $connection }
        Auth              = [ordered]@{ Jwt = [ordered]@{ SigningKey = (New-Secret 48) } }
        Storage           = [ordered]@{ RootPath = (Join-Path $DataPath 'storage') }
        Seed              = [ordered]@{
            SampleData = [bool]$SeedSampleData
            Users      = @([ordered]@{ UserName = $AdminUserName; DisplayName = 'Administrator'; Password = $adminPassword; Role = 'System Administrator' })
        }
    }
    if ($PublicUrl) { $settings['Notifications'] = [ordered]@{ PublicUrl = $PublicUrl } }
    Write-Settings $settings
}

# ---- permissions (need the app pool's identity, so after it exists) -----------------------------------------------

Step 'Setting folder permissions'
Invoke-Icacls $InstallPath /grant "${poolAccount}:(OI)(CI)RX" /T /C /Q
Invoke-Icacls (Join-Path $InstallPath 'logs') /grant "${poolAccount}:(OI)(CI)M" /Q
# Evidence and encryption keys: administrators and this app pool only.
Invoke-Icacls $DataPath /inheritance:r /grant:r "${adminsSid}:(OI)(CI)F" "${systemSid}:(OI)(CI)F" "${poolAccount}:(OI)(CI)M" /Q
# Signing key and connection string.
Invoke-Icacls $settingsPath /inheritance:r /grant:r "${adminsSid}:F" "${systemSid}:F" "${poolAccount}:R" /Q

# ---- site ----------------------------------------------------------------------------------------------------------

Step "Configuring site $SiteName"
$sitePath = "IIS:\Sites\$SiteName"
if (-not (Test-Path $sitePath)) {
    $site = @{ Name = $SiteName; PhysicalPath = $InstallPath; ApplicationPool = $pool; Port = $Port }
    if ($HostName) { $site.HostHeader = $HostName }
    if ($CertificateThumbprint) { $site.Ssl = $true }
    [void](New-Website @site)
    if ($CertificateThumbprint) {
        $protocol = 'https'
        $binding = Get-WebBinding -Name $SiteName -Protocol $protocol
        $binding.AddSslCertificate($CertificateThumbprint, 'my')
    }
}
else {
    Set-ItemProperty $sitePath -Name physicalPath -Value $InstallPath
    Set-ItemProperty $sitePath -Name applicationPool -Value $pool
}
Set-ItemProperty $sitePath -Name applicationDefaults.preloadEnabled -Value $true

if ($OpenFirewall) {
    $rule = "Wings Test Orchestrator ($Port)"
    if (-not (Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue)) {
        [void](New-NetFirewallRule -DisplayName $rule -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow)
    }
}

# ---- start and verify --------------------------------------------------------------------------------------------

Step 'Starting the site (the first start applies database migrations and can take a minute)'
if ((Get-WebAppPoolState -Name $pool).Value -ne 'Started') { Start-WebAppPool -Name $pool }
if ((Get-WebsiteState -Name $SiteName).Value -ne 'Started') { Start-Website -Name $SiteName }
if (-not (Wait-Healthy)) {
    Write-Host ''
    Write-Host 'The site did not answer /health.' -ForegroundColor Red
    Write-Host 'Look in Event Viewer > Windows Logs > Application (sources: IIS AspNetCore Module V2, .NET Runtime).'
    Write-Host "Or set stdoutLogEnabled=""true"" in $InstallPath\web.config and read $InstallPath\logs."
    if ($adminPassword) {
        Write-Host "The first administrator is still pending in $settingsPath (user $AdminUserName). Fix the problem and start the app pool."
    }
    exit 1
}

if ($adminPassword) {
    # The account now exists; the password should not stay on disk.
    $current = Get-Content $settingsPath -Raw | ConvertFrom-Json
    $current.Seed.Users = @()
    Write-Settings $current
}

$scheme = if ($CertificateThumbprint) { 'https' } else { 'http' }
$address = if ($PublicUrl) { $PublicUrl } else { "${scheme}://$(if ($HostName) { $HostName } else { $env:COMPUTERNAME }):$Port" }
Write-Host ''
Write-Host "Wings Test Orchestrator is running at $address" -ForegroundColor Green
if ($adminPassword) {
    Write-Host ''
    Write-Host 'First administrator (shown once, change it after signing in):' -ForegroundColor Yellow
    Write-Host "  User:     $AdminUserName"
    Write-Host "  Password: $adminPassword"
}
Write-Host ''
Write-Host "Back up $DataPath and the database together: the keys folder decrypts environment secrets."
