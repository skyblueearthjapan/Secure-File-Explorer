namespace SecureFileExplorer.Server.Services;

/// <summary>
/// 大量アクセス検知の設定。appsettings の "Alert" セクションにバインドする。
/// しきい値は多段階（注意/警告/異常警告）。警告メールには「誰が・何をしたか」を載せる。
/// </summary>
public sealed class AlertOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>集計の時間窓（分）。例: 30。</summary>
    public int WindowMinutes { get; set; } = 30;

    /// <summary>同一ユーザー・同一以下レベルの連続通知を抑制する時間（分）。</summary>
    public int CooldownMinutes { get; set; } = 30;

    /// <summary>検知の実行間隔（秒）。最小30秒。</summary>
    public int CheckIntervalSeconds { get; set; } = 60;

    /// <summary>メール本文に載せる「直近の操作」明細の最大件数。</summary>
    public int MaxDetailRows { get; set; } = 50;

    /// <summary>通知先メールアドレス（システム管理・総務部など）。</summary>
    public List<string> Recipients { get; set; } = new();

    /// <summary>しきい値レベル（注意/警告/異常警告 など）。Threshold は時間窓内のオープン回数。</summary>
    public List<AlertLevelOption> Levels { get; set; } = new();
}

public sealed class AlertLevelOption
{
    /// <summary>レベル名（例: 注意 / 警告 / 異常警告）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>このレベルに達する、時間窓内のファイルオープン回数。</summary>
    public int Threshold { get; set; }
}
