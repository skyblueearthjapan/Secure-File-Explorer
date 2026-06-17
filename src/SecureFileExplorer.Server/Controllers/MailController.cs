using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureFileExplorer.Contracts;
using SecureFileExplorer.Server.Data;

namespace SecureFileExplorer.Server.Controllers;

/// <summary>
/// 送信待ちメール(アウトボックス)を Outlook 送信エージェントへ受け渡すAPI。
/// サーバーは送信せず、あなたのPC常駐エージェントが取得→Outlookで送信→既読化する。
/// </summary>
[ApiController]
[Authorize] // TODO: 将来は送信エージェント実行アカウント(あなた)・管理者に限定
[Route("api/mail")]
public sealed class MailController : ControllerBase
{
    private readonly AppDbContext _db;

    public MailController(AppDbContext db) => _db = db;

    /// <summary>送信待ちメールを取得する。</summary>
    [HttpGet("pending")]
    public async Task<ActionResult<IReadOnlyList<MailMessageDto>>> Pending([FromQuery] int take = 20, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        var rows = await _db.MailOutbox
            .Where(m => m.Status == MailStatus.Pending)
            .OrderBy(m => m.Id)
            .Take(take)
            .Select(m => new MailMessageDto { Id = m.Id, To = m.ToCsv, Subject = m.Subject, Body = m.Body })
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>送信完了を通知する（エージェントが送信後に呼ぶ）。</summary>
    [HttpPost("{id:long}/sent")]
    public async Task<IActionResult> MarkSent(long id, CancellationToken ct)
    {
        var m = await _db.MailOutbox.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return NotFound();
        m.Status = MailStatus.Sent;
        m.SentUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>送信失敗を通知する（再試行カウント）。</summary>
    [HttpPost("{id:long}/failed")]
    public async Task<IActionResult> MarkFailed(long id, CancellationToken ct)
    {
        var m = await _db.MailOutbox.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return NotFound();
        m.Attempts++;
        if (m.Attempts >= 5) m.Status = MailStatus.Failed; // 5回失敗で打ち切り
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
