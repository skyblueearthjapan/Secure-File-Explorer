namespace SecureFileExplorer.Server.Services;

/// <summary>
/// アクセスログのExcel出力設定。appsettings の "ExcelLog" セクションにバインドする。
/// </summary>
public sealed class ExcelLogOptions
{
    /// <summary>Excel出力を有効にするか。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 出力先の .xlsx パス。
    /// 本番では「総務部が触れないフォルダ」に置く（権限はNTFSで制御。場所は後で確定）。
    /// 例: "D:\\SecureFileExplorerLogs\\access-log.xlsx"
    /// </summary>
    public string FilePath { get; set; } = "logs/access-log.xlsx";

    /// <summary>DB→Excel へ反映する間隔（秒）。最小10秒。</summary>
    public int FlushIntervalSeconds { get; set; } = 60;
}
