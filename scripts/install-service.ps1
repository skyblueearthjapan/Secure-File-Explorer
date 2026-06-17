<#
.SYNOPSIS
  MTSV(LINEWORKS-MTSV) で管理者実行。Secure File Explorer API を Windows サービスとして登録する。
  ※ サービスアカウント(svc-sfe)はAD管理者が事前作成し、技術部共有へ読取権限を付与しておくこと。
  ※ 技術部共有のNTFS権限・中身は変更しない。
.EXAMPLE
  pwsh scripts/install-service.ps1 -ServiceAccount "LINEWORKS\svc-sfe" -Port 5080
#>
param(
  [string]$AppDir        = "D:\Apps\SecureFileExplorer",
  [string]$ServiceName   = "SecureFileExplorer",
  [string]$DisplayName   = "Secure File Explorer API",
  [string]$ServiceAccount,                 # 例: LINEWORKS\svc-sfe（未指定なら確認）
  [int]$Port             = 5080
)

$ErrorActionPreference = "Stop"
$exe = Join-Path $AppDir "SecureFileExplorer.Server.exe"
if (-not (Test-Path $exe)) { throw "実行ファイルが見つかりません: $exe（先に server を配置してください）" }

if (-not $ServiceAccount) { $ServiceAccount = Read-Host "サービスアカウント (例 LINEWORKS\svc-sfe)" }
$cred = Get-Credential -UserName $ServiceAccount -Message "サービスアカウントのパスワードを入力"

# 既存サービスがあれば停止・削除
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
  Write-Host "既存サービスを停止・削除します..." -ForegroundColor Yellow
  Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
  sc.exe delete $ServiceName | Out-Null
  Start-Sleep -Seconds 2
}

Write-Host "サービスを登録します..." -ForegroundColor Cyan
New-Service -Name $ServiceName -BinaryPathName $exe -DisplayName $DisplayName -StartupType Automatic -Credential $cred

# 本番環境変数（ASPNETCORE_ENVIRONMENT=Production）をサービスに設定
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
Set-ItemProperty -Path $regPath -Name Environment -Value @("ASPNETCORE_ENVIRONMENT=Production") -Type MultiString

# ファイアウォール（社内のみ）
$ruleName = "Secure File Explorer API"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
  New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $Port `
    -Action Allow -Profile Domain,Private | Out-Null
  Write-Host "ファイアウォール規則を追加（TCP $Port / Domain,Private）" -ForegroundColor Green
}

Start-Service $ServiceName
Write-Host "完了。状態:" -ForegroundColor Green
Get-Service $ServiceName | Format-Table Name,Status,StartType -AutoSize
Write-Host "疎通確認: curl http://localhost:$Port/api/admin/whoami（要Windows認証）"
