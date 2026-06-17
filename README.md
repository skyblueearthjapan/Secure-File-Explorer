# Secure File Explorer（技術部ファイルビューアー）

Windows Server上の技術部フォルダー・ファイルを、社内ユーザーが **安全に閲覧・1ファイルずつ利用** するための専用アプリケーション。

共有フォルダーを直接開かせないことで、**フォルダー丸ごとの大量コピー・一括持ち出し** を防ぐことを目的とする。完全な情報漏えい防止システムではなく、まずは下記を実現する。

- 共有フォルダーの実パスをユーザーに見せない（`fileId` だけでやり取り）
- Explorer風UIでフォルダー構造を見せる
- 1ファイルずつだけ開ける（既定アプリで起動）
- 一括コピー・ドラッグ＆ドロップ・複数選択を抑止
- 操作ログをサーバーに記録
- 社内ネットワーク内で完結

## 構成

```
Secure-File-Explorer.sln
├─ src/
│  ├─ SecureFileExplorer.Contracts/   共有DTO（実パスを一切含まない）
│  ├─ SecureFileExplorer.Server/      ASP.NET Core Web API（Windows認証 / EF Core+SQLite）
│  └─ SecureFileExplorer.Client/      WPF クライアント（MVVM / Explorer風UI）
└─ tests/
   └─ SecureFileExplorer.Tests/       xUnit テスト
```

### 技術スタック
- .NET 8
- サーバー: ASP.NET Core Web API, EF Core + SQLite, Windows統合認証(Negotiate), IP制限ミドルウェア
- クライアント: WPF (MVVM)

### カタログ方式（オンデマンド / ライブ）
事前の全件スキャンは行わない。ユーザーが開いたフォルダーだけ、サーバーがその場で実ディレクトリを
列挙し、`path↔id` の対応のみを **訪問した分だけDBへ遅延登録** する。これにより、数万〜十数万
ファイル規模の巨大な共有でも初回から瞬時に表示できる（実測: フォルダー展開 0.04〜0.4秒）。

- 横断検索は「これまでに訪問（列挙）したフォルダー」の範囲に限定される。共有全体の検索が必要な
  場合は、将来バックグラウンド索引を追加する（ナビゲーション速度は維持）。

### セキュリティ境界
実パス（`\\server\...`）は **サーバー側のDB(`FullPath`列)とサーバー処理内だけ** が保持する。
クライアントへ返すDTOには `fileId` / `folderId` のみが含まれ、パスは一切含まれない（テストで型レベル保証）。
ファイル取得時は、解決した実パスが **必ず設定ルート配下である** ことを再検証する（パストラバーサル防止）。

## サーバー設定（`src/SecureFileExplorer.Server/appsettings.json`）

```jsonc
{
  "Catalog": {
    "Roots": [
      { "DisplayName": "技術部データ", "Path": "C:\\SecureFileExplorerSample" }
    ]
  },
  "IpRestriction": {
    "Enabled": true,
    "AllowedCidrs": [ "127.0.0.1/32", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" ]
  }
}
```

- `Catalog:Roots` … 公開するルートフォルダー（実パス）。複数指定可。
- `IpRestriction:AllowedCidrs` … 社内LAN/Wi-Fiのレンジ。社外からの接続を遮断する。

## クライアント設定（`src/SecureFileExplorer.Client/appsettings.json`）

```json
{ "ApiBaseUrl": "https://localhost:5001", "AppFolderName": "EngineeringFileViewer" }
```

一時ファイルは `C:\Users\<User>\AppData\Local\<AppFolderName>\Temp\` にランダム名で保存し、
起動時に古いものを削除する（使用中で消せないものは次回起動時に再削除）。

## ビルドと実行

```powershell
# ビルド
dotnet build

# テスト
dotnet test

# サーバー起動（起動時にルートを登録。事前スキャンは不要）
dotnet run --project src/SecureFileExplorer.Server

# クライアント起動
dotnet run --project src/SecureFileExplorer.Client
```

## API 概要（すべて要Windows認証）

| メソッド | パス | 説明 |
|---------|------|------|
| GET  | `/api/folders/roots` | ルートフォルダー一覧 |
| GET  | `/api/folders/{id}/contents` | フォルダーの中身（サブフォルダー・ファイル・パンくず） |
| GET  | `/api/files/{id}/content` | ファイル1件をストリーム取得（実パスは返さない） |
| GET  | `/api/search?q=...` | ファイル名で部分一致検索（訪問済み範囲） |
| GET  | `/api/admin/logs?take=N` | アクセスログ取得（新しい順） |
| POST | `/api/admin/refresh-roots` | ルート登録の確認（オンデマンドのため事前スキャン不要） |
| GET  | `/api/admin/whoami` | 認証確認 |

## ログ

サーバーがDBに記録する項目: ユーザー / PC名 / IP / ファイルID・名 / 操作種別（一覧・オープン・検索・エラー）/ 日時 / 成否 / 失敗理由。
実パスはログにも保存しない。

## 今後の予定（初期MVP対象外）

AD/グループによる細粒度の権限制御、共有全体の横断検索（バックグラウンド索引）、SQL Server移行、
大量アクセス検知、Excel/PDF等のアプリ内プレビュー など。

## ライセンス

社内利用（未定）
