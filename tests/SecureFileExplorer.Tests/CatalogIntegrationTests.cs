using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureFileExplorer.Server.Data;
using SecureFileExplorer.Server.Services;

namespace SecureFileExplorer.Tests;

/// <summary>
/// オンデマンド方式のカタログ検証: ルート登録 → ライブ列挙 → クエリ。
/// 特に「クライアント向けDTOに実パスが含まれない」ことを確認する。
/// </summary>
public sealed class CatalogIntegrationTests : IDisposable
{
    private readonly string _sampleRoot;
    private readonly string _dbPath;

    public CatalogIntegrationTests()
    {
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

    private CatalogService NewCatalog(AppDbContext db)
    {
        var opt = Options.Create(new CatalogOptions
        {
            Roots = new() { new RootFolderConfig { DisplayName = "EngineeringData", Path = _sampleRoot } }
        });
        return new CatalogService(db, opt);
    }

    [Fact]
    public async Task OnDemand_navigation_lists_live_contents_without_real_paths()
    {
        using var db = NewContext();
        var catalog = NewCatalog(db);

        // ルートはオンデマンドで登録される
        var roots = await catalog.GetRootFoldersAsync();
        var root = Assert.Single(roots);
        Assert.Equal("EngineeringData", root.Name);
        Assert.True(root.HasChildren);

        // ルートをライブ列挙
        var contents = await catalog.GetFolderContentsAsync(root.Id);
        Assert.NotNull(contents);
        Assert.Contains(contents!.Folders, f => f.Name == "01_CaseMgmt");
        Assert.Contains(contents.Folders, f => f.Name == "02_Drawings");
        Assert.Contains(contents.Files, f => f.Name == "readme.txt");
        Assert.Equal("EngineeringData", contents.Breadcrumbs.First().Name);

        // 実パス解決はサーバー内部でのみ可能（DTOには無い）
        var readme = contents.Files.First(f => f.Name == "readme.txt");
        var resolved = await catalog.ResolveFilePathAsync(readme.Id);
        Assert.NotNull(resolved);
        Assert.True(File.Exists(resolved!.Value.fullPath));
    }

    [Fact]
    public void Client_dto_types_do_not_expose_any_path_property()
    {
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
    public async Task Search_finds_visited_files_only()
    {
        using var db = NewContext();
        var catalog = NewCatalog(db);

        var roots = await catalog.GetRootFoldersAsync();
        var root = roots[0];

        // 訪問前は索引に無いので検索ヒット0
        var before = await catalog.SearchAsync("estimate", 50);
        Assert.Empty(before);

        // CompanyA まで辿って列挙（= 訪問）すると索引に載る
        var rootContents = await catalog.GetFolderContentsAsync(root.Id);
        var caseMgmt = rootContents!.Folders.First(f => f.Name == "01_CaseMgmt");
        var caseContents = await catalog.GetFolderContentsAsync(caseMgmt.Id);
        var companyA = caseContents!.Folders.First(f => f.Name == "CompanyA");
        await catalog.GetFolderContentsAsync(companyA.Id);

        var after = await catalog.SearchAsync("estimate", 50);
        Assert.Contains(after, h => h.File.Name == "estimate.xlsx");
        Assert.NotEmpty(after.First(h => h.File.Name == "estimate.xlsx").Location);
    }

    [Fact]
    public async Task ResolveFilePath_rejects_ids_outside_configured_roots()
    {
        using var db = NewContext();
        var catalog = NewCatalog(db);

        // 設定ルート外を指す偽ノードを直接仕込む
        var rogue = new CatalogNode
        {
            ParentId = null,
            IsFolder = false,
            Name = "secret.txt",
            FullPath = Path.Combine(Path.GetTempPath(), "outside_root_secret.txt"),
        };
        db.Nodes.Add(rogue);
        await db.SaveChangesAsync();

        var resolved = await catalog.ResolveFilePathAsync(rogue.Id);
        Assert.Null(resolved); // ルート配下でないため拒否される
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_sampleRoot)) Directory.Delete(_sampleRoot, true); } catch { }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
