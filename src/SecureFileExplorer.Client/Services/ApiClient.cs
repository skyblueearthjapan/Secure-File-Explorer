using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using SecureFileExplorer.Contracts;

namespace SecureFileExplorer.Client.Services;

/// <summary>
/// サーバーAPIクライアント。Windows統合認証(既定資格情報)で接続する。
/// クライアントは fileId / folderId のみを扱い、実パスは受け取らない。
/// </summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;

    public ApiClient(ClientConfig config)
    {
        var handler = new HttpClientHandler
        {
            UseDefaultCredentials = true, // ログオン中のWindowsアカウントで認証
            PreAuthenticate = true,
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(config.ApiBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(5),
        };

        // 参考情報としてPC名を送る（サーバー側ログ用）。
        _http.DefaultRequestHeaders.Add("X-Client-Machine", Environment.MachineName);
    }

    public async Task<IReadOnlyList<FolderDto>> GetRootsAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<IReadOnlyList<FolderDto>>("api/folders/roots", ct)
           ?? Array.Empty<FolderDto>();

    public async Task<FolderContentsDto?> GetContentsAsync(long folderId, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<FolderContentsDto>($"api/folders/{folderId}/contents", ct);

    public async Task<IReadOnlyList<FileSearchHitDto>> SearchAsync(string query, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<IReadOnlyList<FileSearchHitDto>>(
               $"api/search?q={Uri.EscapeDataString(query)}", ct)
           ?? Array.Empty<FileSearchHitDto>();

    /// <summary>
    /// fileId のファイル内容を1件ストリーム取得し、指定のローカルパスへ書き出す。
    /// </summary>
    public async Task DownloadFileAsync(long fileId, string destinationPath, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"api/files/{fileId}/content",
            HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 1 << 16, useAsync: true);
        await src.CopyToAsync(dst, ct);
    }

    public void Dispose() => _http.Dispose();
}
