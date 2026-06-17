using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SecureFileExplorer.Server.Data;

namespace SecureFileExplorer.Server.Services;

/// <summary>
/// アクセスログ(DB)を定期的に Excel へ反映するバックグラウンドサービス。
/// ユーザーごとに1シート（シート名＝アカウント名）を作り、新しいログだけ追記する。
/// 反映済みの位置は サイドカーの .state ファイルに記録し、再起動後も続きから処理する。
/// </summary>
public sealed class ExcelLogExporter : BackgroundService
{
    private static readonly string[] Header = { "日時", "操作", "場所(パス)", "対象", "成否", "失敗理由", "PC名", "IP" };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ExcelLogOptions _opt;
    private readonly ILogger<ExcelLogExporter> _log;

    public ExcelLogExporter(IServiceScopeFactory scopeFactory, IOptions<ExcelLogOptions> opt, ILogger<ExcelLogExporter> log)
    {
        _scopeFactory = scopeFactory;
        _opt = opt.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
        {
            _log.LogInformation("ExcelLogExporter is disabled.");
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_opt.FilePath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Excel出力先フォルダを作成できません: {Path}", _opt.FilePath);
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, _opt.FlushIntervalSeconds));
        long lastId = ReadState();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                lastId = await ExportNewAsync(lastId, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException ex)
            {
                // Excel を誰かが開いている等。次回に再試行。
                _log.LogWarning("Excelログ書き込みを延期します（ファイル使用中の可能性）: {Msg}", ex.Message);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Excelログ出力に失敗しました。次回再試行します。");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<long> ExportNewAsync(long lastId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.AccessLogs
            .Where(l => l.Id > lastId)
            .OrderBy(l => l.Id)
            .Take(5000) // 1回の上限（大量時は次回へ）
            .ToListAsync(ct);

        if (rows.Count == 0) return lastId;

        var path = Path.GetFullPath(_opt.FilePath);
        using var wb = File.Exists(path) ? new XLWorkbook(path) : new XLWorkbook();

        foreach (var l in rows)
        {
            var sheetName = SanitizeSheetName(l.UserName);
            var ws = wb.Worksheets.Contains(sheetName) ? wb.Worksheet(sheetName) : CreateSheet(wb, sheetName);

            var next = (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;
            ws.Cell(next, 1).Value = l.TimestampUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
            ws.Cell(next, 2).Value = ActionLabel(l.Action);
            ws.Cell(next, 3).Value = l.TargetPath ?? string.Empty;
            ws.Cell(next, 4).Value = l.Target ?? string.Empty;
            ws.Cell(next, 5).Value = l.Success ? "○" : "×";
            ws.Cell(next, 6).Value = l.FailureReason ?? string.Empty;
            ws.Cell(next, 7).Value = l.MachineName ?? string.Empty;
            ws.Cell(next, 8).Value = l.IpAddress ?? string.Empty;
            lastId = l.Id;
        }

        wb.SaveAs(path);
        WriteState(lastId);
        _log.LogInformation("Excelログを {Count} 件反映しました（lastId={LastId}）。", rows.Count, lastId);
        return lastId;
    }

    private static IXLWorksheet CreateSheet(XLWorkbook wb, string name)
    {
        var ws = wb.Worksheets.Add(name);
        for (int i = 0; i < Header.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = Header[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF1FA");
        }
        ws.SheetView.FreezeRows(1);
        return ws;
    }

    /// <summary>シート名に使えない文字を除き、アカウント名部分を31文字以内にする。</summary>
    private static string SanitizeSheetName(string userName)
    {
        var name = userName ?? string.Empty;
        var slash = name.LastIndexOf('\\');
        if (slash >= 0 && slash < name.Length - 1) name = name[(slash + 1)..];

        foreach (var c in new[] { '\\', '/', '?', '*', '[', ']', ':' })
            name = name.Replace(c, '_');

        name = name.Trim();
        if (name.Length == 0) name = "(unknown)";
        if (name.Length > 31) name = name[..31];
        return name;
    }

    private string StatePath => Path.GetFullPath(_opt.FilePath) + ".state";

    private long ReadState()
    {
        try
        {
            if (File.Exists(StatePath) && long.TryParse(File.ReadAllText(StatePath).Trim(), out var id))
                return id;
        }
        catch { /* 読めなければ0から */ }
        return 0;
    }

    private void WriteState(long lastId)
    {
        try { File.WriteAllText(StatePath, lastId.ToString()); }
        catch (Exception ex) { _log.LogWarning("状態ファイルを書けません: {Msg}", ex.Message); }
    }

    private static string ActionLabel(AccessAction a) => a switch
    {
        AccessAction.ListFolder => "一覧表示",
        AccessAction.OpenFile => "ファイルオープン",
        AccessAction.Search => "検索",
        AccessAction.Error => "エラー",
        _ => a.ToString(),
    };
}
