using System.Text;
using Microsoft.EntityFrameworkCore;
using SecureFileExplorer.Server.Data;

namespace SecureFileExplorer.Server.Services;

/// <summary>
/// 大量アクセス検知のコアロジック（テスト容易にするため BackgroundService から分離）。
/// 時間窓内のファイルオープン回数を多段階しきい値（注意/警告/異常警告）で評価し、
/// 達したユーザーについて、クールダウン・エスカレーションを考慮して警告メールを積む。
/// メール本文には「誰が・何を（どのファイルを）したか」の明細を含める。
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

        // しきい値レベルを昇順に（注意→警告→異常警告）。未設定なら何もしない。
        var levels = _opt.Levels
            .Where(l => l.Threshold > 0)
            .OrderBy(l => l.Threshold)
            .ToList();
        if (levels.Count == 0) return 0;

        var minThreshold = levels[0].Threshold;
        var windowStart = now - TimeSpan.FromMinutes(Math.Max(1, _opt.WindowMinutes));
        var cooldown = TimeSpan.FromMinutes(Math.Max(0, _opt.CooldownMinutes));
        var to = string.Join(",", _opt.Recipients);

        // SQLite は DateTimeOffset の SQL 比較/並べ替えに弱いので、Id 降順で直近を取得し
        // メモリ側で時間窓フィルタ・集計する（直近の高頻度アクセス検知には十分）。
        var recentOpens = (await _db.AccessLogs
                .Where(l => l.Action == AccessAction.OpenFile)
                .OrderByDescending(l => l.Id)
                .Take(20000)
                .ToListAsync(ct))
            .Where(l => l.TimestampUtc >= windowStart)
            .ToList();

        var byUser = recentOpens
            .GroupBy(l => l.UserName)
            .Select(g => new { User = g.Key, Count = g.Count(), Logs = g.OrderByDescending(x => x.TimestampUtc).ToList() })
            .Where(x => x.Count >= minThreshold)
            .ToList();

        int created = 0;
        foreach (var u in byUser)
        {
            // 達した最上位レベル（rank は 1 始まり）
            int rank = 0;
            AlertLevelOption level = levels[0];
            for (int i = 0; i < levels.Count; i++)
            {
                if (u.Count >= levels[i].Threshold) { rank = i + 1; level = levels[i]; }
            }
            if (rank == 0) continue;

            // クールダウン＆エスカレーション: 直近の通知が cooldown 内で、かつ同等以上のレベルなら抑制。
            var last = await _db.MailOutbox
                .Where(m => m.RelatedUser == u.User)
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync(ct);
            if (last is not null && (now - last.CreatedUtc) < cooldown && last.AlertLevel >= rank)
                continue;

            _db.MailOutbox.Add(new MailMessageEntity
            {
                CreatedUtc = now,
                ToCsv = to,
                RelatedUser = u.User,
                AlertLevel = rank,
                Subject = $"【{level.Name}】多数のファイルアクセスを検知: {ShortName(u.User)}",
                Body = BuildBody(u.User, u.Count, level, now, u.Logs),
                Status = MailStatus.Pending,
            });
            created++;
        }

        if (created > 0) await _db.SaveChangesAsync(ct);
        return created;
    }

    private string BuildBody(string user, int count, AlertLevelOption level, DateTimeOffset now, List<AccessLogEntity> logs)
    {
        var newest = logs.FirstOrDefault();
        var sb = new StringBuilder();
        sb.AppendLine("Secure File Explorer で大量のファイルアクセスを検知しました。");
        sb.AppendLine();
        sb.AppendLine($"アカウント : {user}");
        sb.AppendLine($"PC名       : {newest?.MachineName ?? "(不明)"}");
        sb.AppendLine($"IPアドレス : {newest?.IpAddress ?? "(不明)"}");
        sb.AppendLine($"レベル     : {level.Name}（{level.Threshold} 回以上）");
        sb.AppendLine($"検知内容   : 直近 {_opt.WindowMinutes} 分間に {count} 回のファイルオープン");
        sb.AppendLine($"検知時刻   : {now.ToLocalTime():yyyy/MM/dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"― 直近に開いたファイル（新しい順・最大 {_opt.MaxDetailRows} 件）―");

        var shown = logs.Take(Math.Max(1, _opt.MaxDetailRows)).ToList();
        foreach (var l in shown)
        {
            // 「いつ・どのフォルダ階層の・どのファイル」を1行で。場所(パス)が分かれば併記する。
            var where = string.IsNullOrEmpty(l.TargetPath) ? l.Target : $"{l.TargetPath} › {l.Target}";
            sb.AppendLine($"  {l.TimestampUtc.ToLocalTime():yyyy/MM/dd HH:mm:ss}  {where}");
        }
        if (logs.Count > shown.Count)
            sb.AppendLine($"  … 他 {logs.Count - shown.Count} 件");

        sb.AppendLine();
        sb.AppendLine("※ 本メールは自動送信です。全操作の詳細はアクセスログ(Excel)をご確認ください。");
        return sb.ToString();
    }

    private static string ShortName(string user)
    {
        var i = user.LastIndexOf('\\');
        return (i >= 0 && i < user.Length - 1) ? user[(i + 1)..] : user;
    }
}
