using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SecureFileExplorer.Client.ViewModels;

namespace SecureFileExplorer.Client;

/// <summary>
/// メインウィンドウ。Explorer風UIだが、コピー/複数選択/D&Dなどの一括持ち出し操作を抑止する。
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainViewModel vm) : this()
    {
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync();
    }

    /// <summary>ツリーで選択されたフォルダーをViewModelへ伝える。</summary>
    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Vm is not null && e.NewValue is FolderViewModel folder)
            Vm.SelectedFolder = folder;
    }

    /// <summary>ダブルクリックで選択中の1ファイルを開く。</summary>
    private async void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 列ヘッダーや空白部分のダブルクリックを除外
        if (e.OriginalSource is DependencyObject src && ItemsControl.ContainerFromElement(FileList, src) is ListViewItem)
        {
            if (Vm is not null) await Vm.OpenSelectedFileAsync();
        }
    }

    /// <summary>
    /// コピー(Ctrl+C / Ctrl+Insert)・全選択(Ctrl+A)など一括操作系キーを無効化する。
    /// </summary>
    private void FileList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (ctrl && (e.Key == Key.C || e.Key == Key.A || e.Key == Key.X || e.Key == Key.Insert))
        {
            e.Handled = true; // 抑止
            return;
        }
        // Shift+矢印 等による範囲選択の拡張も抑止（SelectionMode=Single だが二重防御）
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift &&
            (e.Key is Key.Up or Key.Down or Key.Home or Key.End))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// 左ボタン押下中のマウス移動（ドラッグ開始）を握りつぶし、ドラッグ＆ドロップによる持ち出しを防ぐ。
    /// </summary>
    private void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            e.Handled = true;
    }
}
