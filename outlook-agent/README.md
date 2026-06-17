# SFE Outlook 送信エージェント

大量アクセス警告メールを、**あなた(imaizumi)のPCの Outlook から**総務部・管理者へ送信するための常駐エージェント。

## なぜサーバーから直接送らないのか
「特定個人のOutlookアカウントから送る」を、サーバーに資格情報を置かずに安全に実現するため。
サーバーは送信せず、警告を**アウトボックス(DB)に積むだけ**。このエージェントが取りに来て送信する。
（既存の Project Management アプリの outlook-agent と同じ考え方）

## 流れ
```
サーバー(MTSV) 大量アクセス検知 → MailOutbox に Pending で積む
        ▲ GET /api/mail/pending          │
        │                                 ▼
あなたのPC: sfe-outlook-agent.ps1 ──Outlook COM──> 送信（あなたのアカウント）
                              └ POST /api/mail/{id}/sent で既読化
```

## 使い方

### 1. まず動作確認（送信せず下書き保存）
```powershell
pwsh outlook-agent/sfe-outlook-agent.ps1 -ApiBaseUrl http://localhost:5062 -DryRun
```
Outlook の「下書き」に入れば成功（実送信しない）。

### 2. スケジュールタスクとして登録（本番）
```powershell
pwsh outlook-agent/install-task.ps1 -ApiBaseUrl http://sfe.lineworks-net.local:5080 -IntervalMinutes 5
```
- あなたのアカウントで数分おきに実行され、送信待ちがあれば送信する。
- Outlook が起動している（またはプロファイルにアクセスできる）こと。

## 注意
- あなたのPCが起動・ログオンしていない間は送信されない（次回実行時にまとめて送信）。
- 宛先は サーバーの `Alert:Recipients`（総務部DL＋管理者）で設定する。
- 送信のしきい値・抑制は サーバーの `Alert` 設定で調整する。
