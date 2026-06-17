<#
.SYNOPSIS
  送信エージェントをスケジュールタスクとして登録する（あなたのPCで実行）。
  数分おきに sfe-outlook-agent.ps1 を実行し、送信待ちメールをあなたのOutlookから送信する。
.EXAMPLE
  pwsh outlook-agent/install-task.ps1 -ApiBaseUrl http://sfe.lineworks-net.local:5080 -IntervalMinutes 5
#>
param(
  [Parameter(Mandatory=$true)][string]$ApiBaseUrl,
  [int]$IntervalMinutes = 5,
  [string]$TaskName = "SFE Outlook Agent"
)

$ErrorActionPreference = "Stop"
$script = Join-Path $PSScriptRoot "sfe-outlook-agent.ps1"
if (-not (Test-Path $script)) { throw "エージェント本体が見つかりません: $script" }

$ps = (Get-Command powershell.exe).Source
$arg = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`" -ApiBaseUrl `"$ApiBaseUrl`""

$action  = New-ScheduledTaskAction -Execute $ps -Argument $arg
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) `
            -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes)
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopOnIdleEnd `
            -ExecutionTimeLimit (New-TimeSpan -Minutes 10)

# 実行ユーザー＝あなた（Outlookプロファイルにアクセスするため対話セッションで動かす）
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
  -Description "Secure File Explorer: 警告メールをOutlookから送信" -Force -RunLevel Limited

Write-Host "登録完了: タスク『$TaskName』が $IntervalMinutes 分間隔で実行されます。" -ForegroundColor Green
Write-Host "まず手動確認: pwsh outlook-agent/sfe-outlook-agent.ps1 -ApiBaseUrl $ApiBaseUrl -DryRun"
