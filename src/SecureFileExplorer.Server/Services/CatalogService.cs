using Microsoft.EntityFrameworkCore;
using SecureFileExplorer.Contracts;
using SecureFileExplorer.Server.Data;

namespace SecureFileExplorer.Server.Services;

public interface ICatalogService
{
    Task<IReadOnlyList<FolderDto>> GetRootFoldersAsync(CancellationToken ct = default);
    Task<FolderContentsDto?> GetFolderContentsAsync(long folderId, CancellationToken ct = default);
    Task<IReadOnlyList<FileSearchHitDto>> SearchAsync(string query, int max, CancellationToken ct = default);

    /// <summary>ファイルの実パスを取得する（サーバー内部専用。ここを通る箇所が機密境界）。</summary>
    Task<(string fullPath, string name)?> ResolveFilePathAsync(long fileId, CancellationToken ct = default);
}

public sealed class CatalogService : ICatalogService
{
    private readonly AppDbContext _db;

    public CatalogService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<FolderDto>> GetRootFoldersAsync(CancellationToken ct = default)
    {
        var roots = await _db.Folders
            .Where(f => f.ParentId == null)
            .OrderBy(f => f.Name)
            .Select(f => new FolderDto
            {
                Id = f.Id,
                ParentId = f.ParentId,
                Name = f.Name,
                HasChildren = f.Children.Any() || f.Files.Any(),
            })
            .ToListAsync(ct);
        return roots;
    }

    public async Task<FolderContentsDto?> GetFolderContentsAsync(long folderId, CancellationToken ct = default)
    {
        var exists = await _db.Folders.AnyAsync(f => f.Id == folderId, ct);
        if (!exists) return null;

        var folders = await _db.Folders
            .Where(f => f.ParentId == folderId)
            .OrderBy(f => f.Name)
            .Select(f => new FolderDto
            {
                Id = f.Id,
                ParentId = f.ParentId,
                Name = f.Name,
                HasChildren = f.Children.Any() || f.Files.Any(),
            })
            .ToListAsync(ct);

        var files = await _db.Files
            .Where(f => f.FolderId == folderId)
            .OrderBy(f => f.Name)
            .Select(f => new FileDto
            {
                Id = f.Id,
                FolderId = f.FolderId,
                Name = f.Name,
                Extension = f.Extension,
                SizeBytes = f.SizeBytes,
                ModifiedUtc = f.ModifiedUtc,
            })
            .ToListAsync(ct);

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

        var pattern = $"%{query}%";
        var hits = await _db.Files
            .Where(f => EF.Functions.Like(f.Name, pattern))
            .OrderBy(f => f.Name)
            .Take(max)
            .Select(f => new FileDto
            {
                Id = f.Id,
                FolderId = f.FolderId,
                Name = f.Name,
                Extension = f.Extension,
                SizeBytes = f.SizeBytes,
                ModifiedUtc = f.ModifiedUtc,
            })
            .ToListAsync(ct);

        var result = new List<FileSearchHitDto>(hits.Count);
        foreach (var hit in hits)
        {
            var location = await BuildBreadcrumbsAsync(hit.FolderId, ct);
            result.Add(new FileSearchHitDto { File = hit, Location = location });
        }
        return result;
    }

    public async Task<(string fullPath, string name)?> ResolveFilePathAsync(long fileId, CancellationToken ct = default)
    {
        var f = await _db.Files
            .Where(x => x.Id == fileId)
            .Select(x => new { x.FullPath, x.Name })
            .FirstOrDefaultAsync(ct);
        return f is null ? null : (f.FullPath, f.Name);
    }

    private async Task<List<BreadcrumbDto>> BuildBreadcrumbsAsync(long folderId, CancellationToken ct)
    {
        // ルートまで親を辿る。フォルダー数は限られるため逐次取得で十分。
        var chain = new List<BreadcrumbDto>();
        long? current = folderId;
        var guard = 0;
        while (current is not null && guard++ < 512)
        {
            var node = await _db.Folders
                .Where(f => f.Id == current)
                .Select(f => new { f.Id, f.Name, f.ParentId })
                .FirstOrDefaultAsync(ct);
            if (node is null) break;
            chain.Add(new BreadcrumbDto { Id = node.Id, Name = node.Name });
            current = node.ParentId;
        }
        chain.Reverse();
        return chain;
    }
}
