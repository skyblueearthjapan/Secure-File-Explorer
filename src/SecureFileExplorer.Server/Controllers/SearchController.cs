using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureFileExplorer.Contracts;
using SecureFileExplorer.Server.Data;
using SecureFileExplorer.Server.Services;

namespace SecureFileExplorer.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/search")]
public sealed class SearchController : ControllerBase
{
    private const int MaxResults = 200;

    private readonly ICatalogService _catalog;
    private readonly IAccessLogger _logger;

    public SearchController(ICatalogService catalog, IAccessLogger logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    /// <summary>ファイル名で検索する（部分一致）。実パスは返さない。</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FileSearchHitDto>>> Search([FromQuery] string q, CancellationToken ct)
    {
        q ??= string.Empty;
        if (q.Trim().Length < 1)
            return Ok(Array.Empty<FileSearchHitDto>());

        var hits = await _catalog.SearchAsync(q, MaxResults, ct);
        await _logger.LogAsync(AccessAction.Search, true, HttpContext, target: q, ct: ct);
        return Ok(hits);
    }
}
