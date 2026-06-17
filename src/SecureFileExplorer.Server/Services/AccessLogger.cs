using Microsoft.AspNetCore.Http;
using SecureFileExplorer.Server.Data;

namespace SecureFileExplorer.Server.Services;

public interface IAccessLogger
{
    Task LogAsync(AccessAction action, bool success, HttpContext http,
        long? fileId = null, long? folderId = null, string? target = null, string? failureReason = null,
        string? targetPath = null, CancellationToken ct = default);
}

/// <summary>
/// アクセスログをDBへ記録する。ユーザー名は認証情報、PC名/IPは接続情報から取得する。
/// 実パスは保存しない（Target には対象名/クエリのみ）。
/// </summary>
public sealed class AccessLogger : IAccessLogger
{
    private readonly AppDbContext _db;

    public AccessLogger(AppDbContext db) => _db = db;

    public async Task LogAsync(AccessAction action, bool success, HttpContext http,
        long? fileId = null, long? folderId = null, string? target = null, string? failureReason = null,
        string? targetPath = null, CancellationToken ct = default)
    {
        var user = http.User?.Identity?.Name ?? "(unknown)";
        var ip = http.Connection.RemoteIpAddress?.ToString();

        // クライアントが任意で送るPC名ヘッダ（任意・検証不可だが参考情報として残す）。
        string? machine = http.Request.Headers.TryGetValue("X-Client-Machine", out var mv)
            ? mv.ToString()
            : null;

        _db.AccessLogs.Add(new AccessLogEntity
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            UserName = user,
            MachineName = machine,
            IpAddress = ip,
            Action = action,
            FileId = fileId,
            FolderId = folderId,
            Target = target,
            TargetPath = targetPath,
            Success = success,
            FailureReason = failureReason,
        });
        await _db.SaveChangesAsync(ct);
    }
}
