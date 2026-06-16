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
    private EntryViewModel? _selectedEntry;
    private string _searchText = string.Empty;
    private string _statusText = "準備完了";
    private string _currentUserLabel = "ログイン: ―";
    private bool _isBusy;
    private long? _currentFolderId;

    public ObservableCollection<FolderViewModel> RootFolders { get; } = new();

    /// <summary>右ペイン。サブフォルダー＋ファイルを Explorer 風に並べる。</summary>
    public ObservableCollection<EntryViewModel> Entries { get; } = new();
    public ObservableCollection<BreadcrumbDto> Breadcrumbs { get; } = new();

    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenEntryCommand { get; }
    public RelayCommand SearchCommand { get; }
    public RelayCommand NavigateBreadcrumbCommand { get; }
    public RelayCommand NavigateUpCommand { get; }

    public MainViewModel(ApiClient api, TempFileManager temp)
    {
        _api = api;
        _temp = temp;

        RefreshCommand = new RelayCommand(_ => RefreshAsync());
        OpenEntryCommand = new RelayCommand(_ => OpenSelectedEntryAsync(), _ => SelectedEntry is not null);
        SearchCommand = new RelayCommand(_ => SearchAsync());
        NavigateBreadcrumbCommand = new RelayCommand(p => NavigateToAsync(p));
        NavigateUpCommand = new RelayCommand(_ => NavigateUpAsync(), _ => Breadcrumbs.Count > 1);
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

    public EntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetField(ref _selectedEntry, value))
            {
                OpenEntryCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedInfo));
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

    public string CurrentUserLabel
    {
        get => _currentUserLabel;
        set => SetField(ref _currentUserLabel, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    /// <summary>下部の情報表示。実パスは出さない。</summary>
    public string SelectedInfo => SelectedEntry?.InfoLabel ?? string.Empty;

    public async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;
            StatusText = "ルートフォルダーを読み込み中...";

            // ログインユーザー表示（Windows統合認証で確定した名前）
            try
            {
                var who = await _api.WhoAmIAsync();
                if (who is not null && !string.IsNullOrEmpty(who.User))
                    CurrentUserLabel = $"ログイン: {who.User}";
            }
            catch { /* 認証情報取得失敗は致命的でないため無視 */ }

            RootFolders.Clear();
            var roots = await _api.GetRootsAsync();

            if (roots.Count == 1)
            {
                // ルートが1つだけのときは、その「ドキュメント」ノード自体は出さず、
                // 配下フォルダーを左ツリーの最上位に並べる（無駄な階層を省く）。
                var rootId = roots[0].Id;
                var contents = await _api.GetContentsAsync(rootId);
                if (contents is not null)
                    foreach (var f in contents.Folders)
                        RootFolders.Add(new FolderViewModel(f, _api));

                await LoadFolderContentsAsync(rootId); // 右ペインにはルート直下の中身を表示
                StatusText = $"フォルダー {RootFolders.Count} 件";
            }
            else
            {
                foreach (var r in roots)
                    RootFolders.Add(new FolderViewModel(r, _api));
                if (roots.Count > 0)
                    await LoadFolderContentsAsync(roots[0].Id);
                StatusText = $"ルートフォルダー {RootFolders.Count} 件";
            }
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

            Entries.Clear();
            Breadcrumbs.Clear();
            if (contents is null) { StatusText = "フォルダーが見つかりません"; return; }

            foreach (var b in contents.Breadcrumbs) Breadcrumbs.Add(b);

            // フォルダーを先、ファイルを後に並べる（Explorer風）
            foreach (var f in contents.Folders) Entries.Add(new EntryViewModel(f));
            foreach (var f in contents.Files) Entries.Add(new EntryViewModel(f));

            NavigateUpCommand.RaiseCanExecuteChanged();
            StatusText = $"フォルダー {contents.Folders.Count} 件 / ファイル {contents.Files.Count} 件";
        }
        catch (Exception ex)
        {
            StatusText = $"読み込み失敗: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>指定フォルダーIDへ移動する（パンくず・右ペイン更新）。</summary>
    public Task NavigateToAsync(object? param)
    {
        long? id = param switch
        {
            BreadcrumbDto b => b.Id,
            long l => l,
            _ => null,
        };
        return id is { } folderId ? LoadFolderContentsAsync(folderId) : Task.CompletedTask;
    }

    private Task NavigateUpAsync()
    {
        if (Breadcrumbs.Count < 2) return Task.CompletedTask;
        var parent = Breadcrumbs[^2]; // 1つ上
        return LoadFolderContentsAsync(parent.Id);
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
            Entries.Clear();
            foreach (var h in hits) Entries.Add(new EntryViewModel(h.File));
            StatusText = $"検索結果: {hits.Count} 件" + (hits.Count >= 200 ? "（上限200件・絞り込んでください）" : "");
        }
        catch (Exception ex)
        {
            StatusText = $"検索失敗: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 選択中の行を開く。フォルダーなら中へ移動、ファイルなら一時取得して既定アプリで開く。
    /// </summary>
    public async Task OpenSelectedEntryAsync()
    {
        var entry = SelectedEntry;
        if (entry is null) return;

        if (entry.IsFolder)
        {
            await LoadFolderContentsAsync(entry.Id);
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = $"\"{entry.Name}\" を取得中...";

            var tempPath = _temp.CreateTempPath(entry.File!.Extension);
            await _api.DownloadFileAsync(entry.Id, tempPath);

            // Windowsのファイル関連付けで既定アプリ起動（中身の解析はしない）。
            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                UseShellExecute = true,
            });

            StatusText = $"\"{entry.Name}\" を開きました";
        }
        catch (Exception ex)
        {
            StatusText = $"ファイルを開けませんでした: {ex.Message}";
        }
        finally { IsBusy = false; }
    }
}
