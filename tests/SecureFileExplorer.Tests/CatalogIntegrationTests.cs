using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecureFileExplorer.Server.Data;
using SecureFileExplorer.Server.Services;

namespace SecureFileExplorer.Tests;

/// <summary>
/// 実フォルダーをスキャン → カタログ構築 → クエリ、までの一連を検証する。
/// 特に「クライアント向けDTOに実パスが含まれない」ことを確認する。
/// </summary>
public sealed class CatalogIntegrationTests : IDisposable
{
    private readonly string _sampleRoot;
    private readonly string _dbPath;

    public CatalogIntegrationTests()
    {
        // 一時的なサンプルフォルダー構成を作る
        _sampleRoot = Path.Combine(Path.GetTempPath(), "sfe_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_sampleRoot, "01_CaseMgmt", "CompanyA"));
        Directory.CreateDirectory(Path.Combine(_sampleRoot, "02_Drawings"));
        File.WriteAllText(Path.Combine(_sampleRoot, "01_CaseMgmt", "CompanyA", "estimate.xlsx"), "dummy");
        File.WriteAllText(Path.Combine(_sampleRoot, "02_Drawings", "drawing1.pdf"), "dummy");
        File.WriteAllText(Path.Combine(_sampleRoot, "readme.txt"), "dummy");

        _dbPath = Path.Combine(Path.GetTempPath(), "sfe_test_" + Guid.NewGuid().ToString("N") + ".db");
    }

    private AppDbContext NewContext()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        var ctx = new AppDbContext(opts);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private CatalogOptions BuildOptions() => new()
    {
        Roots = new() { new RootFolderConfig { DisplayName = "EngineeringData", Path = _sampleRoot } }
    };

    [Fact]
    public async Task Scan_then_query_returns_tree_and_files_without_real_paths()
    {
        // スキャン
        using (var db = NewContext())
        {
            var scanner = new FolderScanner(db, Options.Create(BuildOptions()), NullLogger<FolderScanner>.Instance);
            var result = await scanner.ScanAllAsync();
            Assert.True(result.FilesIndexed >= 3);
            Assert.True(result.FoldersIndexed >= 4); // root + 01 + CompanyA + 02
        }

        // クエリ
        using (var db = NewContext())
        {
            var catalog = new CatalogService(db);

            var roots = await catalog.GetRootFoldersAsync();
            var root = Assert.Single(roots);
            Assert.Equal("EngineeringData", root.Name);
            Assert.True(root.HasChildren);

            var contents = await catalog.GetFolderContentsAsync(root.Id);
            Assert.NotNull(contents);
            Assert.Contains(contents!.Folders, f => f.Name == "01_CaseMgmt");
            Assert.Contains(contents.Files, f => f.Name == "readme.txt");

            // パンくずはルートから始まる
            Assert.Equal("EngineeringData", contents.Breadcrumbs.First().Name);

            // 実パス解決はサーバー内部でのみ可能（クライアントDTOには無い）
            var file = contents.Files.First(f => f.Name == "readme.txt");
            var resolved = await catalog.ResolveFilePathAsync(file.Id);
            Assert.NotNull(resolved);
            Assert.True(File.Exists(resolved!.Value.fullPath));
        }
    }

    [Fact]
    public void Client_dto_types_do_not_expose_any_path_property()
    {
        // DTOに "Path" を含むプロパティが無いことを型レベルで保証する（実パス漏えい防止）。
        var dtoTypes = new[]
        {
            typeof(SecureFileExplorer.Contracts.FileDto),
            typeof(SecureFileExplorer.Contracts.FolderDto),
            typeof(SecureFileExplorer.Contracts.FolderContentsDto),
            typeof(SecureFileExplorer.Contracts.FileSearchHitDto),
            typeof(SecureFileExplorer.Contracts.BreadcrumbDto),
        };

        foreach (var t in dtoTypes)
        {
            foreach (var p in t.GetProperties())
            {
                Assert.DoesNotContain("path", p.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("fullpath", p.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Search_finds_file_by_partial_name()
    {
        using (var db = NewContext())
        {
            var scanner = new FolderScanner(db, Options.Create(BuildOptions()), NullLogger<FolderScanner>.Instance);
            await scanner.ScanAllAsync();
        }

        using (var db = NewContext())
        {
            var catalog = new CatalogService(db);
            var hits = await catalog.SearchAsync("estimate", 50);
            Assert.Contains(hits, h => h.File.Name == "estimate.xlsx");
            // ヒットには階層（パンくず）も付く
            Assert.NotEmpty(hits.First(h => h.File.Name == "estimate.xlsx").Location);
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_sampleRoot)) Directory.Delete(_sampleRoot, true); } catch { }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
