using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureFileExplorer.Contracts;
using SecureFileExplorer.Server.Data;

namespace SecureFileExplorer.Server.Services;

public interface ICatalogService
{
    Task EnsureRootsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FolderDto>> GetRootFoldersAsync(CancellationToken ct = default);
    Task<FolderContentsDto?> GetFolderContentsAsync(long folderId, CancellationToken ct = default);
    Task<IReadOnlyList<FileSearchHitDto>> SearchAsync(string query, int max, CancellationToken ct = default);

    /// <summary>ファイルの実パスを取得する（サーバー内部専用・機密境界）。許可ルート配下のみ返す。</summary>
    Task<(string fullPath, string name)?> ResolveFilePathAsync(long fileId, CancellationToken ct = default);
}

/// <summary>
/// オンデマンド(ライブ)方式のカタログ。事前スキャンせず、要求されたフォルダーをその場で列挙し、
/// path↔id の対応だけをDBへ遅延登録する。実パスは FullPath にのみ保持しクライアントへ出さない。
/// </summary>
public sealed class CatalogService : ICatalogService
{
    private readonly AppDbContext _db;
    private readonly CatalogOptions _opt;
    private readonly HashSet<string> _excludeFolders;
    private readonly HashSet<string> _excludeExt;
    private readonly List<string> _rootFullPaths;

    public CatalogService(AppDbContext db, IOptions<CatalogOptions> opt)
    {
        _db = db;
        _opt = opt.Value;
        _excludeFolders = new HashSet<string>(_opt.ExcludeFolderNames, StringComparer.OrdinalIgnoreCase);
        _excludeExt = new HashSet<string>(_opt.ExcludeExtensions, StringComparer.OrdinalIgnoreCase);
        _rootFullPaths = _opt.Roots
            .Where(r => !string.IsNullOrWhiteSpace(r.Path))
            .Select(r => Path.GetFullPath(r.Path))
            .ToList();
    }

    public async Task EnsureRootsAsync(CancellationToken ct = default)
    {
        foreach (var root in _opt.Roots)
        {
            if (string.IsNullOrWhiteSpace(root.Path)) continue;
            var fullPath = Path.GetFullPath(root.Path);
            var exists = await _db.Nodes.AnyAsync(n => n.ParentId == null && n.FullPath == fullPath, ct);
            if (!exists)
            {
                _db.Nodes.Add(new CatalogNode
                {
                    ParentId = null,
                    IsFolder = true,
                    Name = string.IsNullOrWhiteSpace(root.DisplayName)
                        ? new DirectoryInfo(fullPath).Name
                        : root.DisplayName,
                    FullPath = fullPath,
                });
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FolderDto>> GetRootFoldersAsync(CancellationToken ct = default)
    {
        await EnsureRootsAsync(ct);
        return await _db.Nodes
            .Where(n => n.ParentId == null && n.IsFolder)
            .OrderBy(n => n.Name)
            .Select(n => new FolderDto
            {
                Id = n.Id,
                ParentId = n.ParentId,
                Name = n.Name,
                HasChildren = true, // オンデマンドのため展開可能とみなす（実体は展開時に取得）
            })
            .ToListAsync(ct);
    }

    public async Task<FolderContentsDto?> GetFolderContentsAsync(long folderId, CancellationToken ct = default)
    {
        var node = await _db.Nodes.FirstOrDefaultAsync(n => n.Id == folderId && n.IsFolder, ct);
        if (node is null) return null;
        if (!Directory.Exists(node.FullPath)) return null;

        // この瞬間のディスク内容をライブで列挙する
        var liveDirs = SafeEnumerateDirectories(node.FullPath);
        var liveFiles = SafeEnumerateFiles(node.FullPath);

        // 既存の子ノード（このフォルダーに既に登録済み）を引く
        var existing = await _db.Nodes
            .Where(n => n.ParentId == folderId)
            .ToListAsync(ct);
        var byPath = existing.ToDictionary(n => n.FullPath, StringComparer.OrdinalIgnoreCase);

        var folderNodes = new List<CatalogNode>();
        foreach (var dir in liveDirs)
        {
            if (!byPath.TryGetValue(dir.FullName, out var n))
            {
                n = new CatalogNode { ParentId = folderId, IsFolder = true, Name = dir.Name, FullPath = dir.FullName };
                _db.Nodes.Add(n);
            }
            folderNodes.Add(n);
        }

        var fileNodes = new List<CatalogNode>();
        foreach (var f in liveFiles)
        {
            if (!byPath.TryGetValue(f.FullName, out var n))
            {
                n = new CatalogNode { ParentId = folderId, IsFolder = false, FullPath = f.FullName };
                _db.Nodes.Add(n);
            }
            // メタデータは毎回最新へ更新
            n.Name = f.Name;
            n.Extension = f.Extension;
            n.SizeBytes = f.Size;
            n.ModifiedUtc = f.Modified;
            fileNodes.Add(n);
        }

        await _db.SaveChangesAsync(ct); // 新規ノードに Id を確定

        var folders = folderNodes
            .OrderBy(n => n.Name)
            .Select(n => new FolderDto { Id = n.Id, ParentId = folderId, Name = n.Name, HasChildren = true })
            .ToList();

        var files = fileNodes
            .OrderBy(n => n.Name)
            .Select(n => new FileDto
            {
                Id = n.Id,
                FolderId = folderId,
                Name = n.Name,
                Extension = n.Extension,
                SizeBytes = n.SizeBytes,
                ModifiedUtc = n.ModifiedUtc,
            })
            .ToList();

        var breadcrumbs = await BuildBreadcrumbsAsync(folderId, ct);

        return new FolderContentsDto
        {
            FolderId = folderId,
            Breadcrumbs = breadcrumbs,
            Folders = folders,
            Files = files,
        };
    }

    public async Task<IReadOnlyList<FileSearchHitDto>> SearchAsync(string query, int max, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0) return Array.Empty<FileSearchHitDto>();

        // 注意: オンデマンド方式では「これまで訪問(列挙)したフォルダー」に限り検索できる。
        // 共有全体の横断検索は将来のバックグラウンド索引で対応する。
        var pattern = $"%{query}%";
        var nodes = await _db.Nodes
            .Where(n => !n.IsFolder && EF.Functions.Like(n.Name, pattern))
            .OrderBy(n => n.Name)
            .Take(max)
            .ToListAsync(ct);

        var result = new List<FileSearchHitDto>(nodes.Count);
        foreach (var n in nodes)
        {
            var hit = new FileDto
            {
                Id = n.Id,
                FolderId = n.ParentId ?? 0,
                Name = n.Name,
                Extension = n.Extension,
                SizeBytes = n.SizeBytes,
                ModifiedUtc = n.ModifiedUtc,
            };
            var location = n.ParentId is { } pid ? await BuildBreadcrumbsAsync(pid, ct) : new List<BreadcrumbDto>();
            result.Add(new FileSearchHitDto { File = hit, Location = location });
        }
        return result;
    }

    public async Task<(string fullPath, string name)?> ResolveFilePathAsync(long fileId, CancellationToken ct = default)
    {
        var f = await _db.Nodes
            .Where(x => x.Id == fileId && !x.IsFolder)
            .Select(x => new { x.FullPath, x.Name })
            .FirstOrDefaultAsync(ct);
        if (f is null) return null;

        // 多層防御: 解決した実パスが必ず許可ルート配下であることを確認する
        if (!IsUnderConfiguredRoot(f.FullPath)) return null;

        return (f.FullPath, f.Name);
    }

    private bool IsUnderConfiguredRoot(string fullPath)
    {
        var normalized = Path.GetFullPath(fullPath);
        foreach (var root in _rootFullPaths)
        {
            if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                // ルート自身、またはルート + 区切り文字 配下のみ許可
                if (normalized.Length == root.Length) return true;
                var sep = normalized[root.TrimEnd(Path.DirectorySeparatorChar).Length];
                if (sep == Path.DirectorySeparatorChar || sep == Path.AltDirectorySeparatorChar) return true;
            }
        }
        return false;
    }

    private async Task<List<BreadcrumbDto>> BuildBreadcrumbsAsync(long folderId, CancellationToken ct)
    {
        var chain = new List<BreadcrumbDto>();
        long? current = folderId;
        var guard = 0;
        while (current is not null && guard++ < 512)
        {
            var n = await _db.Nodes
                .Where(x => x.Id == current)
                .Select(x => new { x.Id, x.Name, x.ParentId })
                .FirstOrDefaultAsync(ct);
            if (n is null) break;
            chain.Add(new BreadcrumbDto { Id = n.Id, Name = n.Name });
            current = n.ParentId;
        }
        chain.Reverse();
        return chain;
    }

    // ---- ライブ列挙（問題エントリは個別スキップ）----

    private List<DirectoryInfo> SafeEnumerateDirectories(string path)
    {
        var result = new List<DirectoryInfo>();
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(path); }
        catch { return result; }

        foreach (var d in dirs)
        {
            string name;
            try { name = Path.GetFileName(d); } catch { continue; }
            if (_excludeFolders.Contains(name)) continue;
            try { result.Add(new DirectoryInfo(d)); } catch { /* skip */ }
        }
        return result;
    }

    private List<LiveFile> SafeEnumerateFiles(string path)
    {
        var result = new List<LiveFile>();
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(path); }
        catch { return result; }

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file);
            if (_excludeExt.Contains(ext)) continue;
            try
            {
                var info = new FileInfo(file);
                result.Add(new LiveFile(info.Name, ext, info.FullName, info.Length, info.LastWriteTimeUtc));
            }
            catch { /* クラウド・プレースホルダ等は個別スキップ */ }
        }
        return result;
    }

    private readonly record struct LiveFile(string Name, string Extension, string FullName, long Size, DateTimeOffset Modified);
}
