using Microsoft.EntityFrameworkCore;
using SecureFileExplorer.Server.Data;

namespace SecureFileExplorer.Server.Services;

/// <summary>
/// 大量アクセス検知のコアロジック（テスト容易にするため BackgroundService から分離）。
/// 時間窓内のファイルオープン回数がしきい値を超えたユーザーを検出し、
/// クールダウン中でなければ送信待ちメール(MailOutbox)を1件積む。
/// </summary>
public sealed class AlertDetector
{
    private readonly AppDbContext _db;
    private readonly AlertOptions _opt;

    public AlertDetector(AppDbContext db, AlertOptions opt)
    {
        _db = db;
        _opt = opt;
    }

    /// <summary>1回分の検知を行い、新規に積んだ警告メール件数を返す。</summary>
    public async Task<int> CheckOnceAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        if (!_opt.Enabled) return 0;

        var windowStart = now - TimeSpan.FromMinutes(Math.Max(1, _opt.WindowMinutes));

        // SQLite は DateTimeOffset の SQL 比較/並べ替えに弱いので、Id 降順で直近を取得し
        // メモリ側で時間窓フィルタ・集計する（直近の高頻度アクセス検知には十分）。
        var recent = await _db.AccessLogs
            .Where(l => l.Action == AccessAction.OpenFile)
            .OrderByDescending(l => l.Id)
            .Take(10000)
            .ToListAsync(ct);

        var breaches = recent
            .Where(l => l.TimestampUtc >= windowStart)
            .GroupBy(l => l.UserName)
            .Select(g => new { User = g.Key, Count = g.Count() })
            .Where(x => x.Count >= _opt.MaxOpensInWindow)
            .ToList();

        if (breaches.Count == 0) return 0;

        var cooldown = TimeSpan.FromMinutes(Math.Max(0, _opt.CooldownMinutes));
        var to = string.Join(",", _opt.Recipients);
        int created = 0;

        foreach (var b in breaches)
        {
            // クールダウン: 同一ユーザー宛の直近アラートが cooldown 以内なら積まない
            var last = await _db.MailOutbox
                .Where(m => m.RelatedUser == b.User)
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync(ct);
            if (last is not null && (now - last.CreatedUtc) < cooldown) continue;

            _db.MailOutbox.Add(new MailMessageEntity
            {
                CreatedUtc = now,
                ToCsv = to,
                RelatedUser = b.User,
                Subject = $"【警告】短時間に多数のファイルアクセス: {b.User}",
                Body =
                    $"Secure File Explorer で、短時間に多数のファイルアクセスを検知しました。\n\n" +
                    $"ユーザー: {b.User}\n" +
                    $"検知内容: 直近 {_opt.WindowMinutes} 分間に {b.Count} 回のファイルオープン" +
                    $"（しきい値 {_opt.MaxOpensInWindow} 回）\n" +
                    $"検知時刻: {now.ToLocalTime():yyyy/MM/dd HH:mm:ss}\n\n" +
                    $"※ 本メールは自動送信です。詳細はアクセスログをご確認ください。",
                Status = MailStatus.Pending,
            });
            created++;
        }

        if (created > 0) await _db.SaveChangesAsync(ct);
        return created;
    }
}
