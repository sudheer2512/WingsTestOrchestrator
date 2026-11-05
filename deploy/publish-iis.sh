#!/usr/bin/env bash
# Builds the IIS package: publish/WingsTestOrchestrator-iis-<date>-<commit>.zip (Windows x64, needs the ASP.NET Core 10 Hosting Bundle).
set -euo pipefail
cd "$(dirname "$0")/.."

commit=$(git rev-parse --short HEAD)
[ -z "$(git status --porcelain)" ] || commit="$commit-modified"
name="WingsTestOrchestrator-iis-$(date +%Y%m%d)-$commit"
root="publish/package"
site="$root/$name/site"

rm -rf "$root"
mkdir -p "$site"

echo "==> Building the web UI"
(cd web && { [ -d node_modules ] || npm ci; } && npx ng build)

echo "==> Publishing the API for win-x64"
dotnet publish src/Wings.Api -c Release -r win-x64 --self-contained false -o "$site" --nologo -v quiet

echo "==> Checking the package"
leaks=$(find "$site" \( -name 'appsettings.Development*' -o -name 'appsettings.Production*' -o -name '*.db' -o -name '*.db-*' -o -path '*/App_Data*' \) -print)
if [ -n "$leaks" ]; then
  echo "Refusing to package local settings or data:" >&2
  echo "$leaks" >&2
  exit 1
fi
for f in Wings.Api.dll Wings.Api.exe web.config wwwroot/index.html appsettings.json; do
  [ -e "$site/$f" ] || { echo "Missing from the package: $f" >&2; exit 1; }
done
grep -q 'processPath=".\\Wings.Api.exe" arguments=""' "$site/web.config" || { echo "web.config was not transformed for the app host" >&2; exit 1; }

cp deploy/iis/Install-Wings.ps1 deploy/iis/Uninstall-Wings.ps1 deploy/iis/INSTALL.md "$root/$name/"
(cd "$root" && zip -qr "../$name.zip" "$name")
(cd publish && shasum -a 256 "$name.zip" | tee "$name.zip.sha256")
du -h "publish/$name.zip"
