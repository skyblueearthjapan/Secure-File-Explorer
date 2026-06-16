using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureFileExplorer.Server.Data;

namespace SecureFileExplorer.Server.Services;

public interface IFolderScanner
{
    /// <summary>設定されたルート配下を走査し、フォルダー/ファイル情報をDBへ反映する。</summary>
    Task<ScanResult> ScanAllAsync(CancellationToken ct = default);
}

public sealed record ScanResult(int FoldersIndexed, int FilesIndexed, IReadOnlyList<string> Errors);

/// <summary>
/// ルートフォルダー配下を再帰的に走査し、カタログDBを構築する。
/// 実パスはDBの FullPath にのみ保存し、APIレスポンスには出さない。
/// </summary>
public sealed class FolderScanner : IFolderScanner
{
    private readonly AppDbContext _db;
    private readonly CatalogOptions _opt;
    private readonly ILogger<FolderScanner> _log;

    public FolderScanner(AppDbContext db, IOptions<CatalogOptions> opt, ILogger<FolderScanner> log)
    {
        _db = db;
        _opt = opt.Value;
        _log = log;
    }

    public async Task<ScanResult> ScanAllAsync(CancellationToken ct = default)
    {
        var errors = new List<string>();
        var excludeFolders = new HashSet<string>(_opt.ExcludeFolderNames, StringComparer.OrdinalIgnoreCase);
        var excludeExt = new HashSet<string>(_opt.ExcludeExtensions, StringComparer.OrdinalIgnoreCase);

        // 単純化のため、再スキャン時はカタログを全削除して作り直す（MVP方針）。
        // アクセスログ(AccessLogs)は保持する。将来は差分更新（FileSystemWatcher等）に置き換える。
        await _db.Files.ExecuteDeleteAsync(ct);
        await _db.Folders.ExecuteDeleteAsync(ct);

        int folderCount = 0, fileCount = 0;

        foreach (var root in _opt.Roots)
        {
            if (string.IsNullOrWhiteSpace(root.Path) || !Directory.Exists(root.Path))
            {
                errors.Add($"ルートが見つかりません: {root.DisplayName}");
                continue;
            }

            var rootEntity = new FolderEntity
            {
                ParentId = null,
                Name = string.IsNullOrWhiteSpace(root.DisplayName) ? new DirectoryInfo(root.Path).Name : root.DisplayName,
                FullPath = Path.GetFullPath(root.Path),
                LastScannedUtc = DateTimeOffset.UtcNow,
            };
            _db.Folders.Add(rootEntity);
            await _db.SaveChangesAsync(ct);
            folderCount++;

            await ScanFolderRecursiveAsync(rootEntity, excludeFolders, excludeExt, errors,
                () => folderCount++, () => fileCount++, ct);
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Scan complete. Folders={Folders} Files={Files} Errors={Errors}",
            folderCount, fileCount, errors.Count);

        return new ScanResult(folderCount, fileCount, errors);
    }

    private async Task ScanFolderRecursiveAsync(
        FolderEntity parent,
        HashSet<string> excludeFolders,
        HashSet<string> excludeExt,
        List<string> errors,
        Action onFolder,
        Action onFile,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // ファイル
        try
        {
            foreach (var file in Directory.EnumerateFiles(parent.FullPath))
            {
                var ext = Path.GetExtension(file);
                if (excludeExt.Contains(ext)) continue;

                FileInfo info;
                try { info = new FileInfo(file); }
                catch (Exception ex) { errors.Add($"ファイル読取失敗: {Path.GetFileName(file)} ({ex.Message})"); continue; }

                _db.Files.Add(new FileEntity
                {
                    FolderId = parent.Id,
                    Name = info.Name,
                    Extension = ext,
                    FullPath = info.FullName,
                    SizeBytes = info.Length,
                    ModifiedUtc = info.LastWriteTimeUtc,
                });
                onFile();
            }
        }
        catch (UnauthorizedAccessException) { errors.Add($"アクセス拒否(ファイル): {parent.Name}"); }
        catch (Exception ex) { errors.Add($"列挙失敗(ファイル): {parent.Name} ({ex.Message})"); }

        // サブフォルダー
        List<string> subDirs = new();
        try { subDirs = Directory.EnumerateDirectories(parent.FullPath).ToList(); }
        catch (UnauthorizedAccessException) { errors.Add($"アクセス拒否(フォルダー): {parent.Name}"); }
        catch (Exception ex) { errors.Add($"列挙失敗(フォルダー): {parent.Name} ({ex.Message})"); }

        foreach (var dir in subDirs)
        {
            var name = Path.GetFileName(dir);
            if (excludeFolders.Contains(name)) continue;

            var child = new FolderEntity
            {
                ParentId = parent.Id,
                Name = name,
                FullPath = Path.GetFullPath(dir),
                LastScannedUtc = DateTimeOffset.UtcNow,
            };
            _db.Folders.Add(child);
            await _db.SaveChangesAsync(ct); // Id を確定させてから子を辿る
            onFolder();

            await ScanFolderRecursiveAsync(child, excludeFolders, excludeExt, errors, onFolder, onFile, ct);
        }
    }
}
