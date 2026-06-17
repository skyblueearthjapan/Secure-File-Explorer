using Microsoft.EntityFrameworkCore;
using SecureFileExplorer.Server.Data;
using SecureFileExplorer.Server.Services;

namespace SecureFileExplorer.Tests;

/// <summary>多段階の大量アクセス検知（しきい値・レベル・エスカレーション・明細）の検証。</summary>
public sealed class AlertDetectorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "sfe_alert_" + Guid.NewGuid().ToString("N") + ".db");

    private AppDbContext NewContext()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        var ctx = new AppDbContext(opts);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static AlertOptions Opt() => new()
    {
        Enabled = true,
        WindowMinutes = 30,
        CooldownMinutes = 30,
        MaxDetailRows = 50,
        Recipients = new() { "imaizumi@lineworks.co.jp", "tsujino@lineworks.co.jp" },
        Levels = new()
        {
            new AlertLevelOption { Name = "注意",     Threshold = 10 },
            new AlertLevelOption { Name = "警告",     Threshold = 30 },
            new AlertLevelOption { Name = "異常警告", Threshold = 50 },
        },
    };

    private static void SeedOpens(AppDbContext db, string user, int count, DateTimeOffset at)
    {
        for (int i = 0; i < count; i++)
            db.AccessLogs.Add(new AccessLogEntity
            {
                TimestampUtc = at,
                UserName = user,
                MachineName = "PC-01",
                IpAddress = "192.168.1.50",
                Action = AccessAction.OpenFile,
                Target = $"file_{i}.xlsx",
                TargetPath = "技術部データ › 機械設計 › A社案件",
                Success = true,
            });
        db.SaveChanges();
    }

    [Fact]
    public async Task Below_lowest_threshold_does_not_alert()
    {
        var now = DateTimeOffset.UtcNow;
        using (var db = NewContext()) SeedOpens(db, "LINEWORKS\\a", 9, now); // 注意(10)未満
        using (var db = NewContext())
        {
            Assert.Equal(0, await new AlertDetector(db, Opt()).CheckOnceAsync(now));
            Assert.Equal(0, await db.MailOutbox.CountAsync());
        }
    }

    [Theory]
    [InlineData(12, "注意", 1)]
    [InlineData(35, "警告", 2)]
    [InlineData(60, "異常警告", 3)]
    public async Task Picks_highest_breached_level(int count, string levelName, int rank)
    {
        var now = DateTimeOffset.UtcNow;
        using (var db = NewContext()) SeedOpens(db, "LINEWORKS\\heavy", count, now);
        using (var db = NewContext())
        {
            Assert.Equal(1, await new AlertDetector(db, Opt()).CheckOnceAsync(now));
            var mail = await db.MailOutbox.SingleAsync();
            Assert.Equal(rank, mail.AlertLevel);
            Assert.Contains(levelName, mail.Subject);
            Assert.Contains("heavy", mail.RelatedUser);
            Assert.Contains("imaizumi@lineworks.co.jp", mail.ToCsv);
            // 「誰が・何を」: アカウントと開いたファイル名が本文に含まれる
            Assert.Contains("LINEWORKS\\heavy", mail.Body);
            Assert.Contains("file_", mail.Body);   // 開いたファイルの明細が載っている
            Assert.Contains(".xlsx", mail.Body);
            Assert.Contains("PC-01", mail.Body);
            Assert.Contains("機械設計", mail.Body); // フォルダ階層(パス)が併記される
        }
    }

    [Fact]
    public async Task Same_level_within_cooldown_is_suppressed()
    {
        var now = DateTimeOffset.UtcNow;
        using (var db = NewContext()) SeedOpens(db, "LINEWORKS\\h", 12, now); // 注意
        using (var db = NewContext())
        {
            var d = new AlertDetector(db, Opt());
            Assert.Equal(1, await d.CheckOnceAsync(now));
            Assert.Equal(0, await d.CheckOnceAsync(now.AddMinutes(1))); // 同レベルは抑制
            Assert.Equal(1, await db.MailOutbox.CountAsync());
        }
    }

    [Fact]
    public async Task Escalation_to_higher_level_sends_again()
    {
        var now = DateTimeOffset.UtcNow;
        using (var db = NewContext()) SeedOpens(db, "LINEWORKS\\h", 12, now); // 注意
        using (var db = NewContext())
        {
            var d = new AlertDetector(db, Opt());
            Assert.Equal(1, await d.CheckOnceAsync(now)); // 注意 送信

            // さらにアクセスが増えて 警告 レベルへ → クールダウン中でもエスカレーションで再送
            SeedOpens(db, "LINEWORKS\\h", 25, now.AddMinutes(2)); // 計37 → 警告
            Assert.Equal(1, await d.CheckOnceAsync(now.AddMinutes(3)));

            var mails = await db.MailOutbox.OrderBy(m => m.Id).ToListAsync();
            Assert.Equal(2, mails.Count);
            Assert.Equal(1, mails[0].AlertLevel); // 注意
            Assert.Equal(2, mails[1].AlertLevel); // 警告
        }
    }

    [Fact]
    public async Task Old_opens_outside_window_are_ignored()
    {
        var now = DateTimeOffset.UtcNow;
        using (var db = NewContext()) SeedOpens(db, "LINEWORKS\\h", 20, now.AddMinutes(-45)); // 窓(30分)外
        using (var db = NewContext())
            Assert.Equal(0, await new AlertDetector(db, Opt()).CheckOnceAsync(now));
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
