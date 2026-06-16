using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureFileExplorer.Contracts;
using SecureFileExplorer.Server.Data;
using SecureFileExplorer.Server.Services;

namespace SecureFileExplorer.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/folders")]
public sealed class FoldersController : ControllerBase
{
    private readonly ICatalogService _catalog;
    private readonly IAccessLogger _logger;

    public FoldersController(ICatalogService catalog, IAccessLogger logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    /// <summary>ルートフォルダー一覧（ツリーの最上位）。</summary>
    [HttpGet("roots")]
    public async Task<ActionResult<IReadOnlyList<FolderDto>>> GetRoots(CancellationToken ct)
    {
        var roots = await _catalog.GetRootFoldersAsync(ct);
        await _logger.LogAsync(AccessAction.ListFolder, true, HttpContext, target: "roots", ct: ct);
        return Ok(roots);
    }

    /// <summary>指定フォルダーの中身（サブフォルダー・ファイル・パンくず）。</summary>
    [HttpGet("{id:long}/contents")]
    public async Task<ActionResult<FolderContentsDto>> GetContents(long id, CancellationToken ct)
    {
        var contents = await _catalog.GetFolderContentsAsync(id, ct);
        if (contents is null)
        {
            await _logger.LogAsync(AccessAction.ListFolder, false, HttpContext, folderId: id,
                failureReason: "folder not found", ct: ct);
            return NotFound();
        }

        await _logger.LogAsync(AccessAction.ListFolder, true, HttpContext, folderId: id, ct: ct);
        return Ok(contents);
    }
}
