namespace SecureFileExplorer.Server.Data;

/// <summary>
/// path↔id の対応を保持するカタログノード（フォルダー/ファイル兼用）。
/// オンデマンド方式では、ユーザーが訪問したフォルダーの子だけが遅延登録される。
/// 実パス(FullPath)はサーバー内部のみで保持し、クライアントへは決して返さない。
/// </summary>
public class CatalogNode
{
    public long Id { get; set; }
    public long? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>サーバー上の実フルパス。機密。クライアントへ露出禁止。</summary>
    public string FullPath { get; set; } = string.Empty;

    public bool IsFolder { get; set; }

    // ファイル用メタデータ（列挙時に更新する）。フォルダーでは既定値。
    public string Extension { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
}

/// <summary>操作種別。</summary>
public enum AccessAction
{
    ListFolder = 0,
    OpenFile = 1,
    Search = 2,
    Error = 3,
}

/// <summary>送信メールの状態。</summary>
public enum MailStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
}

/// <summary>
/// 送信待ちメール（アウトボックス）。サーバーは直接送信せず、ここへ積む。
/// あなたのPC常駐エージェントが取りに来て、あなたのOutlookから送信する。
/// </summary>
public class MailMessageEntity
{
    public long Id { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>宛先（カンマ区切り）。総務部DL＋管理者など。</summary>
    public string ToCsv { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>関連ユーザー（同一ユーザーへの連続通知抑制＝クールダウン判定に使う）。</summary>
    public string? RelatedUser { get; set; }

    public MailStatus Status { get; set; } = MailStatus.Pending;
    public DateTimeOffset? SentUtc { get; set; }
    public int Attempts { get; set; }
}

/// <summary>
/// アクセスログ。ユーザー・PC名・IP・対象・成否などを記録する。
/// </summary>
public class AccessLogEntity
{
    public long Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }

    public string UserName { get; set; } = string.Empty;
    public string? MachineName { get; set; }
    public string? IpAddress { get; set; }

    public AccessAction Action { get; set; }

    public long? FileId { get; set; }
    public long? FolderId { get; set; }

    /// <summary>対象名やクエリ文字列など（実パスは保存しない）。</summary>
    public string? Target { get; set; }

    public bool Success { get; set; }
    public string? FailureReason { get; set; }
}
