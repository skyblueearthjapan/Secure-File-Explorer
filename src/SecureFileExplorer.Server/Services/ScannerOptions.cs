namespace SecureFileExplorer.Server.Services;

/// <summary>
/// 公開するルートフォルダーなどの設定。appsettings.json の "Catalog" セクションにバインドする。
/// </summary>
public sealed class CatalogOptions
{
    /// <summary>
    /// 公開対象のルートフォルダー実パス一覧。
    /// 例: ["\\\\server\\engineering", "D:\\技術部データ"]
    /// </summary>
    public List<RootFolderConfig> Roots { get; set; } = new();

    /// <summary>スキャン時に除外するフォルダー名（大小無視）。</summary>
    public List<string> ExcludeFolderNames { get; set; } = new() { ".git", "$RECYCLE.BIN", "System Volume Information" };

    /// <summary>スキャン時に除外する拡張子（先頭ドット付き・大小無視）。</summary>
    public List<string> ExcludeExtensions { get; set; } = new() { ".tmp", ".lnk" };
}

public sealed class RootFolderConfig
{
    /// <summary>ツリー上に表示する名前。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>サーバー上の実パス（機密・クライアントへ露出禁止）。</summary>
    public string Path { get; set; } = string.Empty;
}
