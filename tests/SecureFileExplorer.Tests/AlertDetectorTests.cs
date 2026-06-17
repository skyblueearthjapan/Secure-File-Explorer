using Microsoft.EntityFrameworkCore;
using SecureFileExplorer.Server.Data;
using SecureFileExplorer.Server.Services;

namespace SecureFileExplorer.Tests;

/// <summary>大量アクセス検知（しきい値・クールダウン）の検証。</summary>
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
        WindowMinutes = 10,
        MaxOpensInWindow = 5,
        CooldownMinutes = 60,
        Recipients = new() { "soumu@example.local", "admin@example.local" },
    };

    private static void SeedOpens(AppDbContext db, string user, int count, DateTimeOffset at)
    {
        for (int i = 0; i < count; i++)
            db.AccessLogs.Add(new AccessLogEntity
            {
                TimestampUtc = at,
                UserName = user,
                Action = AccessAction.OpenFile,
                Success = true,
            });
        db.SaveChanges();
    }

    [Fact]
    public async Task Breach_over_threshold_queues_one_alert_with_recipients()
    {
        var now = DateTimeOffset.UtcNow;
        using (var db = NewContext())
            SeedOpens(db, "LINEWORKS\\heavy", 6, now); // しきい値5を超える

        using (var db = NewContext())
        {
            var created = await new AlertDetector(db, Opt()).CheckOnceAsync(now);
            Assert.Equal(1, created);

            var mail = await db.MailOutbox.SingleAsync();
            Assert.Equal(MailStatus.Pending, mail.Status);
            Assert.Contains("heavy", mail.RelatedUser);
            Assert.Contains("soumu@example.local", mail.ToCsv);
            Assert.Contains("admin@example.local", mail.ToCsv);
            Assert.Contains("6", mail.Body); // 件数が本文に含まれる
        }
    }

    [Fact]
    public async Task Under_threshold_does_not_alert()
    {
        var now = DateTimeOffset.UtcNow;
        using (var db = NewContext())
            SeedOpens(db, "LINEWORKS\\light", 3, now); // しきい値未満

        using (var db = NewContext())
        {
            var created = await new AlertDetector(db, Opt()).CheckOnceAsync(now);
            Assert.Equal(0, created);
            Assert.Equal(0, await db.MailOutbox.CountAsync());
        }
    }

    [Fact]
    public async Task Cooldown_suppresses_duplicate_alerts_for_same_user()
    {
        var now = DateTimeOffset.UtcNow;
        using (var db = NewContext())
            SeedOpens(db, "LINEWORKS\\heavy", 6, now);

        using (var db = NewContext())
        {
            var d = new AlertDetector(db, Opt());
            Assert.Equal(1, await d.CheckOnceAsync(now));
            // 直後の再チェックはクールダウンで抑制される
            Assert.Equal(0, await d.CheckOnceAsync(now.AddMinutes(1)));
            Assert.Equal(1, await db.MailOutbox.CountAsync());
        }
    }

    [Fact]
    public async Task Old_opens_outside_window_are_ignored()
    {
        var now = DateTimeOffset.UtcNow;
        using (var db = NewContext())
            SeedOpens(db, "LINEWORKS\\heavy", 6, now.AddMinutes(-30)); // 窓(10分)の外

        using (var db = NewContext())
        {
            var created = await new AlertDetector(db, Opt()).CheckOnceAsync(now);
            Assert.Equal(0, created);
        }
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
