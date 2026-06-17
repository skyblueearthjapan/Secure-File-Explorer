namespace SecureFileExplorer.Server.Services;

/// <summary>
/// 大量アクセス検知の設定。appsettings の "Alert" セクションにバインドする。
/// </summary>
public sealed class AlertOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>集計の時間窓（分）。</summary>
    public int WindowMinutes { get; set; } = 10;

    /// <summary>時間窓内のファイルオープン回数がこの値以上で警告。</summary>
    public int MaxOpensInWindow { get; set; } = 30;

    /// <summary>同一ユーザーへの連続通知を抑制する時間（分）。</summary>
    public int CooldownMinutes { get; set; } = 60;

    /// <summary>検知の実行間隔（秒）。最小30秒。</summary>
    public int CheckIntervalSeconds { get; set; } = 60;

    /// <summary>通知先メールアドレス（総務部DL・管理者など）。</summary>
    public List<string> Recipients { get; set; } = new();
}
