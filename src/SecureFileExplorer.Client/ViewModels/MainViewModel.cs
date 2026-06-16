using System.Collections.ObjectModel;
using System.Diagnostics;
using SecureFileExplorer.Client.Services;
using SecureFileExplorer.Contracts;

namespace SecureFileExplorer.Client.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ApiClient _api;
    private readonly TempFileManager _temp;

    private FolderViewModel? _selectedFolder;
    private FileItemViewModel? _selectedFile;
    private string _searchText = string.Empty;
    private string _statusText = "準備完了";
    private bool _isBusy;
    private long? _currentFolderId;

    public ObservableCollection<FolderViewModel> RootFolders { get; } = new();
    public ObservableCollection<FileItemViewModel> Files { get; } = new();
    public ObservableCollection<BreadcrumbDto> Breadcrumbs { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenFileCommand { get; }
    public RelayCommand SearchCommand { get; }

    public MainViewModel(ApiClient api, TempFileManager temp)
    {
        _api = api;
        _temp = temp;

        RefreshCommand = new RelayCommand(_ => RefreshAsync());
        OpenFileCommand = new RelayCommand(_ => OpenSelectedFileAsync(), _ => SelectedFile is not null);
        SearchCommand = new RelayCommand(_ => SearchAsync());
    }

    public FolderViewModel? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetField(ref _selectedFolder, value) && value is not null)
                _ = LoadFolderContentsAsync(value.Id);
        }
    }

    public FileItemViewModel? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetField(ref _selectedFile, value))
            {
                OpenFileCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedFileInfo));
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set => SetField(ref _searchText, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    /// <summary>下部のファイル情報表示。実パスは出さない。</summary>
    public string SelectedFileInfo => SelectedFile is null
        ? string.Empty
        : $"{SelectedFile.Name}   |   種類: {SelectedFile.TypeLabel}   |   サイズ: {SelectedFile.SizeLabel}   |   更新: {SelectedFile.ModifiedLabel}";

    public async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = "ルートフォルダーを読み込み中...";
            RootFolders.Clear();
            var roots = await _api.GetRootsAsync();
            foreach (var r in roots)
                RootFolders.Add(new FolderViewModel(r, _api));
            StatusText = $"ルートフォルダー {RootFolders.Count} 件";
        }
        catch (Exception ex)
        {
            StatusText = $"サーバー接続に失敗しました: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private async Task LoadFolderContentsAsync(long folderId)
    {
        try
        {
            IsBusy = true;
            _currentFolderId = folderId;
            var contents = await _api.GetContentsAsync(folderId);
            Files.Clear();
            Breadcrumbs.Clear();
            if (contents is null) { StatusText = "フォルダーが見つかりません"; return; }

            foreach (var b in contents.Breadcrumbs) Breadcrumbs.Add(b);
            foreach (var f in contents.Files) Files.Add(new FileItemViewModel(f));
            StatusText = $"{contents.Files.Count} 個のファイル";
        }
        catch (Exception ex)
        {
            StatusText = $"読み込み失敗: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private Task RefreshAsync()
        => _currentFolderId is { } id ? LoadFolderContentsAsync(id) : InitializeAsync();

    private async Task SearchAsync()
    {
        var q = SearchText.Trim();
        if (q.Length == 0) { if (_currentFolderId is { } id) await LoadFolderContentsAsync(id); return; }

        try
        {
            IsBusy = true;
            StatusText = $"\"{q}\" を検索中...";
            var hits = await _api.SearchAsync(q);
            Files.Clear();
            foreach (var h in hits) Files.Add(new FileItemViewModel(h.File));
            StatusText = $"検索結果: {hits.Count} 件" + (hits.Count >= 200 ? "（上限200件・絞り込んでください）" : "");
        }
        catch (Exception ex)
        {
            StatusText = $"検索失敗: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 選択中の1ファイルだけを一時取得し、Windowsの既定アプリで開く。
    /// </summary>
    public async Task OpenSelectedFileAsync()
    {
        var file = SelectedFile;
        if (file is null) return;

        try
        {
            IsBusy = true;
            StatusText = $"\"{file.Name}\" を取得中...";

            var tempPath = _temp.CreateTempPath(file.Dto.Extension);
            await _api.DownloadFileAsync(file.Id, tempPath);

            // Windowsのファイル関連付けで既定アプリ起動（中身の解析はしない）。
            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true,
            });

            StatusText = $"\"{file.Name}\" を開きました";
        }
        catch (Exception ex)
        {
            StatusText = $"ファイルを開けませんでした: {ex.Message}";
        }
        finally { IsBusy = false; }
    }
}
