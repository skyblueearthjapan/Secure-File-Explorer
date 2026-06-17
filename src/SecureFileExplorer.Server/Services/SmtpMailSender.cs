using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using SecureFileExplorer.Server.Data;

namespace SecureFileExplorer.Server.Services;

/// <summary>
/// 送信待ちメール(MailOutbox)を Gmail/Google Workspace の SMTP で直接送信するバックグラウンドサービス。
/// クライアント常駐エージェントは不要。送信成否を MailOutbox に記録する。
/// </summary>
public sealed class SmtpMailSender : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SmtpOptions _opt;
    private readonly ILogger<SmtpMailSender> _log;

    public SmtpMailSender(IServiceScopeFactory scopeFactory, IOptions<SmtpOptions> opt, ILogger<SmtpMailSender> log)
    {
        _scopeFactory = scopeFactory;
        _opt = opt.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
        {
            _log.LogInformation("SmtpMailSender is disabled (Smtp:Enabled=false).");
            return;
        }
        if (string.IsNullOrWhiteSpace(_opt.User) || string.IsNullOrWhiteSpace(_opt.Password))
        {
            _log.LogWarning("SMTP の User/Password が未設定です。送信できません。");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, _opt.PollSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DrainOnceAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning(ex, "SMTP送信処理に失敗しました。次回再試行します。"); }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task DrainOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = await db.MailOutbox
            .Where(m => m.Status == MailStatus.Pending)
            .OrderBy(m => m.Id)
            .Take(20)
            .ToListAsync(ct);
        if (pending.Count == 0) return;

        using var client = new SmtpClient();
        var secure = _opt.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
        await client.ConnectAsync(_opt.Host, _opt.Port, secure, ct);
        await client.AuthenticateAsync(_opt.User, _opt.Password, ct);

        var fromAddr = string.IsNullOrWhiteSpace(_opt.FromAddress) ? _opt.User : _opt.FromAddress;

        foreach (var m in pending)
        {
            try
            {
                var msg = new MimeMessage();
                msg.From.Add(new MailboxAddress(_opt.FromName, fromAddr));
                foreach (var to in SplitAddresses(m.ToCsv))
                    msg.To.Add(MailboxAddress.Parse(to));
                if (msg.To.Count == 0) throw new InvalidOperationException("宛先が空です");

                msg.Subject = m.Subject;
                msg.Body = new TextPart("plain") { Text = m.Body };

                await client.SendAsync(msg, ct);

                m.Status = MailStatus.Sent;
                m.SentUtc = DateTimeOffset.UtcNow;
                _log.LogWarning("警告メールを送信しました id={Id} 宛先={To} 件名={Subject}", m.Id, m.ToCsv, m.Subject);
            }
            catch (Exception ex)
            {
                m.Attempts++;
                if (m.Attempts >= 5) m.Status = MailStatus.Failed;
                _log.LogWarning("メール送信失敗 id={Id} attempts={Attempts}: {Msg}", m.Id, m.Attempts, ex.Message);
            }
            await db.SaveChangesAsync(ct);
        }

        await client.DisconnectAsync(true, ct);
    }

    private static IEnumerable<string> SplitAddresses(string csv)
        => (csv ?? string.Empty)
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
