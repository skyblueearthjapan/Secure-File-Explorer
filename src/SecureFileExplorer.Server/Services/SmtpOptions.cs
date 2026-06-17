namespace SecureFileExplorer.Server.Services;

/// <summary>
/// SMTP送信設定（Gmail/Google Workspace 直送）。appsettings の "Smtp" セクションにバインドする。
/// パスワードは Google の「アプリ パスワード」。秘密情報なので本番サーバーの設定にのみ置く（gitに入れない）。
/// </summary>
public sealed class SmtpOptions
{
    public bool Enabled { get; set; } = false;

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;

    /// <summary>認証ユーザー（例: imaizumi@lineworks-local.info）。</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Google アプリ パスワード（16桁）。秘密。本番設定のみ。</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>差出人アドレス。空なら User を使う。</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>差出人表示名。</summary>
    public string FromName { get; set; } = "Secure File Explorer";

    /// <summary>送信待ちを処理する間隔（秒）。最小10秒。</summary>
    public int PollSeconds { get; set; } = 30;
}
