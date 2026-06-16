using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using SecureFileExplorer.Server.Data;
using SecureFileExplorer.Server.Services;

namespace SecureFileExplorer.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/files")]
public sealed class FilesController : ControllerBase
{
    private readonly ICatalogService _catalog;
    private readonly IAccessLogger _logger;
    private readonly ILogger<FilesController> _log;

    public FilesController(ICatalogService catalog, IAccessLogger logger, ILogger<FilesController> log)
    {
        _catalog = catalog;
        _logger = logger;
        _log = log;
    }

    /// <summary>
    /// fileId のファイルを「1つだけ」ストリームで返す。
    /// 実パスはレスポンスに含めず、Content-Disposition のファイル名のみ返す。
    /// 一括取得を防ぐため、複数IDの受け取りや範囲指定は提供しない。
    /// </summary>
    [HttpGet("{id:long}/content")]
    public async Task<IActionResult> GetContent(long id, CancellationToken ct)
    {
        var resolved = await _catalog.ResolveFilePathAsync(id, ct);
        if (resolved is null)
        {
            await _logger.LogAsync(AccessAction.OpenFile, false, HttpContext, fileId: id,
                failureReason: "file not found in catalog", ct: ct);
            return NotFound();
        }

        var (fullPath, name) = resolved.Value;

        if (!System.IO.File.Exists(fullPath))
        {
            // カタログにはあるが実体が消えている等。実パスはログに残さない。
            await _logger.LogAsync(AccessAction.OpenFile, false, HttpContext, fileId: id, target: name,
                failureReason: "file missing on server", ct: ct);
            return NotFound();
        }

        if (!new FileExtensionContentTypeProvider().TryGetContentType(name, out var contentType))
            contentType = "application/octet-stream";

        await _logger.LogAsync(AccessAction.OpenFile, true, HttpContext, fileId: id, target: name, ct: ct);

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 16, useAsync: true);
        // ファイル名のみ提示。enableRangeProcessing は無効（部分取得・ミラーリングを助長しないため）。
        return File(stream, contentType, fileDownloadName: name, enableRangeProcessing: false);
    }
}
