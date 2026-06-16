namespace SecureFileExplorer.Contracts;

/// <summary>
/// フォルダーを表すDTO。実パスは一切含めない。クライアントは Id のみを使ってAPIに問い合わせる。
/// </summary>
public sealed record FolderDto
{
    public long Id { get; init; }
    public long? ParentId { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>子フォルダーを持つか（ツリーの遅延展開用）。</summary>
    public bool HasChildren { get; init; }
}

/// <summary>
/// ファイルを表すDTO。実パスは含めず Id のみで識別する。
/// </summary>
public sealed record FileDto
{
    public long Id { get; init; }
    public long FolderId { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>拡張子（先頭ドットを含む。例: ".xlsx"）。関連付け起動の判断に使う。</summary>
    public string Extension { get; init; } = string.Empty;

    public long SizeBytes { get; init; }
    public DateTimeOffset ModifiedUtc { get; init; }
}

/// <summary>パンくず1要素。</summary>
public sealed record BreadcrumbDto
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// 1フォルダーの中身（パンくず・サブフォルダー・ファイル一覧）。
/// </summary>
public sealed record FolderContentsDto
{
    public long FolderId { get; init; }
    public IReadOnlyList<BreadcrumbDto> Breadcrumbs { get; init; } = Array.Empty<BreadcrumbDto>();
    public IReadOnlyList<FolderDto> Folders { get; init; } = Array.Empty<FolderDto>();
    public IReadOnlyList<FileDto> Files { get; init; } = Array.Empty<FileDto>();
}

/// <summary>検索結果1件（どのフォルダーにあるかも返す）。</summary>
public sealed record FileSearchHitDto
{
    public FileDto File { get; init; } = new();
    public IReadOnlyList<BreadcrumbDto> Location { get; init; } = Array.Empty<BreadcrumbDto>();
}

/// <summary>現在の認証ユーザー情報。</summary>
public sealed record WhoAmIDto
{
    public string User { get; init; } = string.Empty;
    public bool Authenticated { get; init; }
}

/// <summary>アクセスログ1件（閲覧用）。実パスは含まない。</summary>
public sealed record AccessLogDto
{
    public long Id { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string? MachineName { get; init; }
    public string? IpAddress { get; init; }

    /// <summary>操作種別の表示名（一覧表示 / ファイルオープン / 検索 / エラー）。</summary>
    public string Action { get; init; } = string.Empty;

    public long? FileId { get; init; }
    public long? FolderId { get; init; }
    public string? Target { get; init; }
    public bool Success { get; init; }
    public string? FailureReason { get; init; }
}
