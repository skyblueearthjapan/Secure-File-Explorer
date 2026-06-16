using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using SecureFileExplorer.Client.Services;
using SecureFileExplorer.Contracts;

namespace SecureFileExplorer.Client;

public partial class LogWindow : Window, INotifyPropertyChanged
{
    private readonly ApiClient _api;
    private string _statusText = string.Empty;

    public ObservableCollection<LogRow> Rows { get; } = new();

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LogWindow(ApiClient api)
    {
        _api = api;
        InitializeComponent();
        DataContext = this;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async System.Threading.Tasks.Task ReloadAsync()
    {
        try
        {
            StatusText = "読み込み中...";
            var logs = await _api.GetLogsAsync(200);
            Rows.Clear();
            foreach (var l in logs) Rows.Add(new LogRow(l));
            StatusText = $"{Rows.Count} 件";
        }
        catch (System.Exception ex)
        {
            StatusText = $"取得失敗: {ex.Message}";
        }
    }
}

/// <summary>ログ表示用の整形済み行。</summary>
public sealed class LogRow
{
    public LogRow(AccessLogDto l)
    {
        Time = l.TimestampUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
        UserName = l.UserName;
        Action = l.Action;
        Target = l.Target ?? string.Empty;
        Result = l.Success ? "○" : "×";
        MachineName = l.MachineName ?? string.Empty;
        IpAddress = l.IpAddress ?? string.Empty;
    }

    public string Time { get; }
    public string UserName { get; }
    public string Action { get; }
    public string Target { get; }
    public string Result { get; }
    public string MachineName { get; }
    public string IpAddress { get; }
}
