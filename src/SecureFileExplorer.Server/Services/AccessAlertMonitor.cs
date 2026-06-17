using Microsoft.Extensions.Options;
using SecureFileExplorer.Server.Data;

namespace SecureFileExplorer.Server.Services;

/// <summary>
/// 定期的に大量アクセスを検知し、警告メールをアウトボックスへ積むバックグラウンドサービス。
/// </summary>
public sealed class AccessAlertMonitor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertOptions _opt;
    private readonly ILogger<AccessAlertMonitor> _log;

    public AccessAlertMonitor(IServiceScopeFactory scopeFactory, IOptions<AlertOptions> opt, ILogger<AccessAlertMonitor> log)
    {
        _scopeFactory = scopeFactory;
        _opt = opt.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
        {
            _log.LogInformation("AccessAlertMonitor is disabled.");
            return;
        }
        if (_opt.Recipients.Count == 0)
            _log.LogWarning("Alert:Recipients が未設定です。検知しても送信先がありません。");

        var interval = TimeSpan.FromSeconds(Math.Max(30, _opt.CheckIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var detector = new AlertDetector(db, _opt);
                var n = await detector.CheckOnceAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (n > 0) _log.LogWarning("大量アクセス警告を {Count} 件キューに追加しました。", n);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "大量アクセス検知に失敗しました。次回再試行します。");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
