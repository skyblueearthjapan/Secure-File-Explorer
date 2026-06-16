using System.Windows;
using SecureFileExplorer.Client.Services;
using SecureFileExplorer.Client.ViewModels;

namespace SecureFileExplorer.Client;

public partial class App : Application
{
    private ApiClient? _api;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var config = ClientConfig.Load();
        var temp = new TempFileManager(config);

        // 起動時に古い一時ファイルを削除（使用中で消せないものは次回起動時に再試行）。
        temp.CleanupOldFiles();

        _api = new ApiClient(config);
        var vm = new MainViewModel(_api, temp);

        var window = new MainWindow(vm);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _api?.Dispose();
        base.OnExit(e);
    }
}
