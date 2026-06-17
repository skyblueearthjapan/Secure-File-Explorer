<#
.SYNOPSIS
  Secure File Explorer の送信待ちメールを、あなたのPCの Outlook から送信するエージェント。
  サーバーのアウトボックスをポーリング → Outlook COM で送信 → 送信済みに更新する。
  スケジュールタスクで数分おきに実行する想定（1回実行して終了）。

.PARAMETER ApiBaseUrl
  サーバーAPIのベースURL（例: http://sfe.lineworks-net.local:5080）

.PARAMETER DryRun
  指定すると送信せず Outlook の「下書き」として保存（動作確認用）。

.EXAMPLE
  pwsh outlook-agent/sfe-outlook-agent.ps1 -ApiBaseUrl http://localhost:5062 -DryRun
#>
param(
  [Parameter(Mandatory=$true)][string]$ApiBaseUrl,
  [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$base = $ApiBaseUrl.TrimEnd('/')

function Get-Pending {
  Invoke-RestMethod -Uri "$base/api/mail/pending?take=20" -UseDefaultCredentials
}
function Mark-Sent($id)   { Invoke-RestMethod -Uri "$base/api/mail/$id/sent"   -Method POST -UseDefaultCredentials | Out-Null }
function Mark-Failed($id) { Invoke-RestMethod -Uri "$base/api/mail/$id/failed" -Method POST -UseDefaultCredentials | Out-Null }

$pending = @(Get-Pending)
if ($pending.Count -eq 0) { Write-Host "送信待ちメールはありません。"; return }

Write-Host "送信待ち: $($pending.Count) 件"

# Outlook を取得（起動していなければ起動）
$outlook = New-Object -ComObject Outlook.Application

foreach ($m in $pending) {
  try {
    if ([string]::IsNullOrWhiteSpace($m.to)) { throw "宛先が空です（Alert:Recipients 未設定）" }
    $mail = $outlook.CreateItem(0)  # 0 = olMailItem
    $mail.To      = $m.to
    $mail.Subject = $m.subject
    $mail.Body    = $m.body
    if ($DryRun) {
      $mail.Save()   # 下書きに保存（送信しない）
      Write-Host "  [DryRun] 下書き保存: id=$($m.id) -> $($m.to)"
    } else {
      $mail.Send()
      Write-Host "  送信: id=$($m.id) -> $($m.to)"
    }
    Mark-Sent $m.id
  }
  catch {
    Write-Warning "  失敗 id=$($m.id): $($_.Exception.Message)"
    try { Mark-Failed $m.id } catch {}
  }
}
