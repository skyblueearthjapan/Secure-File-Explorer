# MTSV デプロイ Runbook（Secure File Explorer）

社内 Windows Server **MTserver = LINEWORKS-MTSV (192.168.1.242)** へ、本アプリのサーバー(API)を
**Windowsサービス**として配置し、社内ネットワークで運用するための設計・手順書。

> 既存2アプリ（Stock / Project Management）は Ubuntu VM 上の Docker/Traefik による
> **ブラウザ型 Python アプリ**。本アプリは **.NET + WPFデスクトップ + Windows統合認証** のため、
> インフラ（設置サーバー・ファイルサーバー・サービスアカウント・DNS・ファイアウォール）の流儀は
> 踏襲しつつ、サーバーは **MTSV 上の Windows サービス** として動かす（[ホスト方式の判断は決定済み: A]）。

---

## 環境の前提（既存調査より）

| 役割 | ホスト | 補足 |
|------|--------|------|
| アプリサーバー設置先 | **LINEWORKS-MTSV / 192.168.1.242** | Windows Server 2019 |
| ファイルサーバー(DC) | **LINEWORKS-SV / 192.168.1.240** | `\\lineworks-sv\Data`。技術部共有もここ |
| 対象データ | `\\lineworks-sv\Data\技術部` | **読み取り専用。NTFS権限・中身は変更しない** |
| 社内LAN | 192.168.1.0/24 | ファイアウォールは Domain/Private のみ許可 |

### MTSV 実測スペック（2026-06-17 取得）
- OS: Windows Server 2019 Standard / CPU: Xeon Silver 4215R 16論理コア（余裕）
- **RAM: 7.6 GB（空き約1.2 GB）** ← 既存VM群が消費。逼迫気味。**VMではなくWindowsサービス**を選ぶ根拠。
- ディスク **C: 60GB（空き約16GB）** ← 少なめ。アプリは置かない。
- ディスク **D: 9.0TB（空き約9.0TB）** ← **アプリ・ログはここD:に置く。**

> 配置方針: 本アプリ一式は **D:ドライブ** に置く（C:は空きが少ないため）。RAMが逼迫しているので
> 起動後にメモリ使用量を確認し、不足ならMTSVのRAM増設 or 既存VMの割当調整を検討する。

---

## フェーズ1: サーバーを Windows サービスとして配置

### 1-0. 事前にコードへ加える小改修（このフェーズで実装）
- `Microsoft.Extensions.Hosting.WindowsServices` を追加し、`builder.Host.UseWindowsService()` を有効化。
- Kestrel の待受を明示（例: `http://0.0.0.0:5080`）。`appsettings.Production.json` で上書き。
- 既存の IP制限ミドルウェア(`IpRestriction`)＋Windows認証はそのまま使用。

### 1-1. サービスアカウント（※AD管理者が手動。実共有のNTFSは触らない）
- 専用アカウント `svc-sfe`（例）を AD に作成（他アプリの `svc-zaiko`/`svc-dove` と同流儀）。
- `\\lineworks-sv\Data\技術部` への **読み取り権限のみ** を付与（管理者がNTFS設定。我々は実施しない）。
- 「代理アクセス方式」: ユーザーはこのサービス経由でのみ閲覧。将来、共有のNTFSを
  `svc-sfe`＋管理者のみに絞ると、Explorer直開きを完全に塞げる（運用準備後・手動で）。

### 1-2. 発行（publish）
開発機で:
```powershell
dotnet publish src/SecureFileExplorer.Server -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o publish/server
```
- self-contained にして MTSV 側に .NET ランタイム導入を不要化（推奨）。

### 1-3. MTSV へ配置
- `publish/server` を MTSV の `D:\Apps\SecureFileExplorer\` へコピー（RDP もしくは管理共有）。
- `appsettings.Production.json` を作成し、`Catalog:Roots` を `\\lineworks-sv\Data\技術部`、
  `IpRestriction:AllowedCidrs` に社内レンジ、待受ポートを設定。

### 1-4. Windows サービス登録（MTSV 上・管理者PowerShell）
```powershell
New-Service -Name "SecureFileExplorer" `
  -BinaryPathName "D:\Apps\SecureFileExplorer\SecureFileExplorer.Server.exe" `
  -DisplayName "Secure File Explorer API" -StartupType Automatic
# サービスアカウントで実行
sc.exe config SecureFileExplorer obj= "LINEWORKS\svc-sfe" password= "<pw>"
Start-Service SecureFileExplorer
```

### 1-5. ファイアウォール（MTSV）
```powershell
New-NetFirewallRule -DisplayName "Secure File Explorer API" `
  -Direction Inbound -Protocol TCP -LocalPort 5080 -Action Allow -Profile Domain,Private
```

### 1-6. 認証(SPN)・名前解決
- Windows統合認証(Negotiate/Kerberos)をサービスアカウントで使うため、必要に応じて SPN 登録:
  `setspn -S HTTP/lineworks-mtsv LINEWORKS\svc-sfe`（管理者）。NTLMフォールバックでも当面可。
- 社内DNS に Aレコード（例 `sfe.lineworks-net.local` → 192.168.1.242）。無ければIP直でも可。
- 将来HTTPS化（社内CAの証明書）を推奨。当面はLAN内HTTPでも可。

### 1-7. クライアント配布（各PC）
- `dotnet publish src/SecureFileExplorer.Client -c Release -r win-x64 --self-contained true`。
- 配布物の `appsettings.json` の `ApiBaseUrl` を `http://sfe.lineworks-net.local:5080`（or IP）に。
- 配布方法: 共有フォルダ設置 / MSI / ClickOnce など（Project Management の per-PC exe 配布と同様の実績あり）。

### 1-8. 受け入れ確認
- 別PCのクライアントから接続 → 技術部ツリー表示 / ファイルを既定アプリで開ける。
- `/api/admin/whoami` で各ユーザー名が正しく取得できる（＝2点目のユーザー別ログの土台）。

---

## フェーズ2: ユーザー別ログを Excel 出力（1人1シート）

- サーバー(.NET)に `ClosedXML` を追加。アクセスログ(DBの AccessLogs)を Excel へ反映。
- **1ユーザー＝1シート**（シート名＝アカウント名）。列: 日時/操作/対象/成否/PC名/IP。
- 出力先: **MTSV のローカル**（例 `D:\SecureFileExplorerLogs\access-log.xlsx`）。技術部共有には書かない。
- 反映方式の候補: (a) 定期ジョブ（数分間隔でDB→ExcL追記） / (b) 日次バッチ。まず (a) を想定。
- 検討事項: Excel をプロセスが開きっぱなしにしない（生成は短時間ロック）、肥大化時のシート/ファイル分割。

## フェーズ3: しきい値超過の検知

- ルール例（設定可能に）:「同一ユーザーが N 分間に M 回以上 OpenFile」または「1日 K 回以上」。
- サーバー側で集計し、超過したら **アラート**を1件生成（重複通知の抑制クールダウン付き）。
- 設定は `appsettings` の `AlertRules` セクションに（しきい値・期間・対象操作）。

## フェーズ4: あなたの Outlook から総務部＋管理者へメール

- Project Management の **outlook-agent 方式**を流用:
  1. サーバーが超過アラート → 送信ジョブ（宛先=総務部+管理者、件名/本文=該当ユーザー・件数・期間）を作成。
  2. **あなたのPC常駐の小さな送信エージェント**（.NET/PyいずれでもOK。Outlook COM 使用）がジョブを取得。
  3. **あなたの Outlook アカウントから .Send()** で送信。
- サーバーから直接SMTP送信ではなく常駐agent経由にする理由: 「特定個人のOutlookから送る」を
  安全に実現する社内の確立パターンだから（資格情報をサーバーに置かない）。
- 検討事項: あなたのPCが起動・Outlook起動・agent常駐していること。宛先リスト（総務部DL＋管理者）。
  送信頻度の抑制（スパム化防止）。

---

## フェーズ1着手前に確定したい項目

| 項目 | 例/候補 |
|------|---------|
| サービスアカウント名 | `svc-sfe`（AD管理者が作成・共有へ読取権限付与） |
| 待受ポート | `5080`（他サービスと衝突しない値） |
| ホスト名/DNS | `sfe.lineworks-net.local` → 192.168.1.242（無ければIP直） |
| HTTP/HTTPS | 当面HTTP（LAN内）→ 将来HTTPS（社内CA） |
| publish形態 | self-contained（ランタイム導入不要） |
| クライアント配布方法 | 共有フォルダ / MSI / ClickOnce |

## 2点目 着手前に確定したい項目

| 項目 | 例/候補 |
|------|---------|
| しきい値 | 例: 10分で30回 / 1日100回（要相談） |
| Excel出力先 | `D:\SecureFileExplorerLogs\access-log.xlsx`（ローカル） |
| 通知宛先 | 総務部DL + 管理者アドレス |
| 送信元 | あなた(imaizumi)のOutlook、常駐agent経由 |

