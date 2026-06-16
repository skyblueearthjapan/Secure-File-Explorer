namespace SecureFileExplorer.Server.Data;

/// <summary>
/// フォルダーのカタログ行。実パス(FullPath)はサーバー内部のみで保持し、
/// クライアントへは決して返さない。
/// </summary>
public class FolderEntity
{
    public long Id { get; set; }
    public long? ParentId { get; set; }
    public FolderEntity? Parent { get; set; }
    public ICollection<FolderEntity> Children { get; set; } = new List<FolderEntity>();
    public ICollection<FileEntity> Files { get; set; } = new List<FileEntity>();

    public string Name { get; set; } = string.Empty;

    /// <summary>サーバー上の実フルパス。機密。クライアントへ露出禁止。</summary>
    public string FullPath { get; set; } = string.Empty;

    public DateTimeOffset LastScannedUtc { get; set; }
}

/// <summary>
/// ファイルのカタログ行。実パス(FullPath)はサーバー内部のみ。
/// </summary>
public class FileEntity
{
    public long Id { get; set; }
    public long FolderId { get; set; }
    public FolderEntity? Folder { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;

    /// <summary>サーバー上の実フルパス。機密。クライアントへ露出禁止。</summary>
    public string FullPath { get; set; } = string.Empty;

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
