using System.IO;
using System.Text.Json;

namespace SecureFileExplorer.Client.Services;

/// <summary>
/// クライアント設定。実行ファイルと同じ場所の appsettings.json から読み込む。
/// </summary>
public sealed class ClientConfig
{
    /// <summary>サーバーAPIのベースURL。例: "https://server.contoso.local:5001"</summary>
    public string ApiBaseUrl { get; set; } = "https://localhost:5001";

    /// <summary>一時ファイルを置くアプリ名フォルダー。</summary>
    public string AppFolderName { get; set; } = "EngineeringFileViewer";

    public static ClientConfig Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<ClientConfig>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (cfg is not null) return cfg;
            }
        }
        catch
        {
            // 設定読込失敗時は既定値で続行する。
        }
        return new ClientConfig();
    }
}
