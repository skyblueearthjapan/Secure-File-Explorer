using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureFileExplorer.Server.Services;

namespace SecureFileExplorer.Server.Controllers;

[ApiController]
[Authorize] // TODO: 将来は管理者グループに限定（Windows認証のロールで制御）
[Route("api/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly IFolderScanner _scanner;

    public AdminController(IFolderScanner scanner) => _scanner = scanner;

    /// <summary>ルートフォルダーを再スキャンしてカタログを更新する。</summary>
    [HttpPost("scan")]
    public async Task<ActionResult<ScanResult>> Scan(CancellationToken ct)
    {
        var result = await _scanner.ScanAllAsync(ct);
        return Ok(result);
    }

    /// <summary>現在のユーザー情報（疎通・認証確認用）。</summary>
    [HttpGet("whoami")]
    public ActionResult<object> WhoAmI()
        => Ok(new { user = User.Identity?.Name, authenticated = User.Identity?.IsAuthenticated ?? false });
}
