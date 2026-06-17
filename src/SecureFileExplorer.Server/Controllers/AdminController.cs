using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureFileExplorer.Contracts;
using SecureFileExplorer.Server.Data;
using SecureFileExplorer.Server.Services;

namespace SecureFileExplorer.Server.Controllers;

[ApiController]
[Authorize] // TODO: 将来は管理者グループに限定（Windows認証のロールで制御）
[Route("api/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly ICatalogService _catalog;
    private readonly AppDbContext _db;

    public AdminController(ICatalogService catalog, AppDbContext db)
    {
        _catalog = catalog;
        _db = db;
    }

    /// <summary>
    /// ルート登録を確認する（オンデマンド方式のため事前スキャンは不要）。
    /// 現在キャッシュ済み（訪問済み）のノード数を返す。
    /// </summary>
    [HttpPost("refresh-roots")]
    public async Task<ActionResult<object>> RefreshRoots(CancellationToken ct)
    {
        await _catalog.EnsureRootsAsync(ct);
        var known = await _db.Nodes.CountAsync(ct);
        return Ok(new { knownNodes = known, mode = "on-demand" });
    }

    /// <summary>現在のユーザー情報（疎通・認証確認用）。</summary>
    [HttpGet("whoami")]
    public ActionResult<WhoAmIDto> WhoAmI()
        => Ok(new WhoAmIDto
        {
            User = User.Identity?.Name ?? string.Empty,
            Authenticated = User.Identity?.IsAuthenticated ?? false,
        });

    /// <summary>直近のアクセスログを取得する（新しい順）。</summary>
    [HttpGet("logs")]
    public async Task<ActionResult<IReadOnlyList<AccessLogDto>>> Logs([FromQuery] int take = 200, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);
        // SQLite は DateTimeOffset の ORDER BY 非対応。Id(自動採番) は挿入順=時刻順なので Id 降順で代用。
        var rows = await _db.AccessLogs
            .OrderByDescending(l => l.Id)
            .Take(take)
            .ToListAsync(ct);

        var logs = rows.Select(l => new AccessLogDto
        {
            Id = l.Id,
            TimestampUtc = l.TimestampUtc,
            UserName = l.UserName,
            MachineName = l.MachineName,
            IpAddress = l.IpAddress,
            Action = ActionLabel(l.Action),
            FileId = l.FileId,
            FolderId = l.FolderId,
            Target = l.Target,
            Success = l.Success,
            FailureReason = l.FailureReason,
        }).ToList();

        return Ok(logs);
    }

    private static string ActionLabel(AccessAction a) => a switch
    {
        AccessAction.ListFolder => "一覧表示",
        AccessAction.OpenFile => "ファイルオープン",
        AccessAction.Search => "検索",
        AccessAction.Error => "エラー",
        _ => a.ToString(),
    };
}
