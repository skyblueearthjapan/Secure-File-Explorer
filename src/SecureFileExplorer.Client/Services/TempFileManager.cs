using System.IO;
using System.Security.Cryptography;

namespace SecureFileExplorer.Client.Services;

/// <summary>
/// 一時ファイルの保存・削除を管理する。
/// - 推測されにくいランダム名を使う
/// - 実サーバーパスは名前に含めない（拡張子のみ保持して関連付け起動できるようにする）
/// - 起動時に古い一時ファイルを削除（使用中で消せないものは次回起動時に再試行）
/// </summary>
public sealed class TempFileManager
{
    private readonly string _tempRoot;

    public TempFileManager(ClientConfig config)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _tempRoot = Path.Combine(local, config.AppFolderName, "Temp");
        Directory.CreateDirectory(_tempRoot);
    }

    public string TempRoot => _tempRoot;

    /// <summary>
    /// 指定拡張子で、推測されにくいランダム名の一時ファイルパスを生成する（まだ作成はしない）。
    /// </summary>
    public string CreateTempPath(string extension)
    {
        if (!string.IsNullOrEmpty(extension) && !extension.StartsWith('.'))
            extension = "." + extension;

        // 16バイトの暗号乱数 → URL安全な英数字。実ファイル名やパスの痕跡を残さない。
        Span<byte> buf = stackalloc byte[16];
        RandomNumberGenerator.Fill(buf);
        var token = Convert.ToHexString(buf).ToLowerInvariant();

        return Path.Combine(_tempRoot, token + extension);
    }

    /// <summary>
    /// 一時フォルダー内の既存ファイルを削除する。使用中で削除できないものはスキップ（次回起動時に再試行）。
    /// </summary>
    public CleanupResult CleanupOldFiles()
    {
        int deleted = 0, skipped = 0;
        if (!Directory.Exists(_tempRoot)) return new CleanupResult(0, 0);

        foreach (var file in Directory.EnumerateFiles(_tempRoot))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
                deleted++;
            }
            catch
            {
                // 開いている最中などで削除できない場合は次回に回す。
                skipped++;
            }
        }
        return new CleanupResult(deleted, skipped);
    }
}

public sealed record CleanupResult(int Deleted, int Skipped);
