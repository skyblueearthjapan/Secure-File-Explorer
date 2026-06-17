<#
.SYNOPSIS
  開発機で実行。サーバー(自己完結exe)とクライアントを publish/ に発行する。
  MTSV にランタイム導入は不要（self-contained）。
.EXAMPLE
  pwsh scripts/publish.ps1
#>
param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [string]$OutDir = "publish"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "== サーバーを発行 ==" -ForegroundColor Cyan
dotnet publish "$root/src/SecureFileExplorer.Server" -c $Configuration -r $Runtime --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o "$root/$OutDir/server"

Write-Host "== クライアントを発行 ==" -ForegroundColor Cyan
dotnet publish "$root/src/SecureFileExplorer.Client" -c $Configuration -r $Runtime --self-contained true `
  -o "$root/$OutDir/client"

Write-Host ""
Write-Host "完了。次の手順:" -ForegroundColor Green
Write-Host "  1. $OutDir/server を MTSV の D:\Apps\SecureFileExplorer\ にコピー"
Write-Host "  2. appsettings.Production.json.sample を appsettings.Production.json にコピーして値を設定"
Write-Host "     （環境変数 ASPNETCORE_ENVIRONMENT=Production をサービスに設定）"
Write-Host "  3. MTSV で scripts/install-service.ps1 を管理者実行"
Write-Host "  4. $OutDir/client を各PCへ配布（appsettings.json の ApiBaseUrl を設定）"
