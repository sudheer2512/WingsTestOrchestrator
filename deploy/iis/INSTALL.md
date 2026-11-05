# Installing Wings Test Orchestrator in IIS

## Prerequisites on the server

- Windows Server 2016 or later, or Windows 10/11 with IIS, x64
- [ASP.NET Core 10 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/10.0). If IIS is not enabled yet, run
  this package's script first (it enables IIS, then stops and tells you to install the bundle), because the bundle must
  be installed after IIS. If you installed it first, repair it from *Apps & features*.
- SQL Server 2019 or later, unless you evaluate with `-DatabaseProvider Sqlite`

## Install

Extract the zip, open PowerShell **as Administrator** in the extracted folder, and run one of these:

```powershell
# Local SQL Server, HTTP on port 8080, reachable from other machines
powershell -ExecutionPolicy Bypass -File .\Install-Wings.ps1 -Port 8080 -OpenFirewall

# HTTPS with a host name and a certificate from LocalMachine\My
powershell -ExecutionPolicy Bypass -File .\Install-Wings.ps1 -Port 443 -HostName wto.wings.local -CertificateThumbprint <thumbprint> -PublicUrl https://wto.wings.local

# Evaluation without SQL Server: SQLite plus labelled sample data
powershell -ExecutionPolicy Bypass -File .\Install-Wings.ps1 -DatabaseProvider Sqlite -SeedSampleData
```

The script prints the address and the first administrator's password **once**. Change that password after signing in.

| Parameter | Default | Meaning |
|---|---|---|
| `-Port`, `-HostName`, `-CertificateThumbprint` | 8080, none, none | Site binding (HTTPS when a thumbprint is given) |
| `-PublicUrl` | none | Address used in email, Teams and tracker links |
| `-InstallPath` | `C:\inetpub\WingsTestOrchestrator` | Site files |
| `-DataPath` | `C:\ProgramData\WingsTestOrchestrator` | Evidence, packages, encryption keys |
| `-SqlServer`, `-DatabaseName` | `.`, `WingsTestOrchestrator` | Local or named instance reached with your Windows sign-in |
| `-ConnectionString` | none | Remote server or SQL login (see below) |
| `-DatabaseProvider` | `SqlServer` | `SqlServer` or `Sqlite` |
| `-OpenFirewall` | off | Allow the port inbound; agents on other machines need it |
| `-SeedSampleData` | off | Add the labelled sample Wings products |
| `-SiteName`, `-AdminUserName`, `-SkipFeatureInstall` | | Site and pool name, administrator name, leave IIS features alone |

### SQL Server

With `-SqlServer`, the script uses **your** Windows sign-in (sysadmin, or dbcreator plus securityadmin) to create the
database and grant `IIS AppPool\WingsTestOrchestrator` the `db_owner` role, which the site needs to apply migrations.

An app pool identity only works on the same machine. For a **remote** SQL Server, create an empty database and a SQL login
that owns it, then pass
`-ConnectionString "Server=SQL01;Database=WingsTestOrchestrator;User Id=wings;Password=...;Encrypt=True;TrustServerCertificate=True"`.

If policy forbids `db_owner` for the site, run `schema.sql` from the repository's `database/sqlserver` folder, grant
`db_datareader` and `db_datawriter`, and set `Database:MigrateOnStartup` to `false`.

## Upgrade

Extract the newer package and run its `Install-Wings.ps1` with the same `-SiteName` and paths. Settings, data and the
database are kept, migrations run on start, and the previous files stay in `<DataPath>\previous-site`.
**Back up the database first**: rolling back the files does not roll back migrations.

## After installing

Settings live in `<InstallPath>\appsettings.Production.json`, readable only by administrators and the app pool, and are
never replaced by an upgrade. Recycle the app pool after editing it. Common additions:

```json
{
  "Notifications": {
    "PublicUrl": "https://wto.wings.local",
    "Smtp": { "Host": "smtp.wings.local", "Port": 587, "EnableSsl": true, "UserName": "wto", "Password": "...", "From": "wto@wings.local" }
  },
  "Ai": { "ApiKey": "..." },
  "Scanning": { "ClamAv": { "Host": "clamd.wings.local", "Port": 3310 } }
}
```

Without an SMTP host, emails are written as `.eml` files to `<DataPath>\storage\mail-outbox`. The AI assistant also needs
switching on under *Administration → AI assistant policy*.

**Back up `<DataPath>` together with the database.** Environment secrets cannot be decrypted without its `storage\keys`
folder. For keys protected at rest, see the Data Protection note in the repository README.

Execution agents are installed separately on test machines: see `docs/agent-installation.md` in the repository.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| HTTP 500.19 | Hosting Bundle missing or installed before IIS: repair it, then `iisreset` |
| HTTP 500.30 or 500.3x | The app failed to start, usually the database. Read Event Viewer → Application, or set `stdoutLogEnabled="true"` in `web.config` and read the `logs` folder (set it back afterwards) |
| HTTP 503 | The app pool stopped after repeated start failures: fix the cause, then start the pool |
| Sign-in works but live updates show offline | WebSockets feature disabled, or a proxy in front of IIS does not pass them |
| HTTP 404.13 on uploads | A server-level request limit is lower than the site's 650 MB |
| Runs stop being dispatched after idle time | The app pool must have Start mode *AlwaysRunning* and Idle time-out 0; the installer sets both |

The app must be its own IIS site, not a virtual application under another site, because the UI uses root-relative paths.

## Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall-Wings.ps1
```

This keeps the data folder and the database. Add `-RemoveData` to delete the data folder as well.
