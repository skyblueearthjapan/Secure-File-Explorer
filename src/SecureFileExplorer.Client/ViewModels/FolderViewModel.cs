using System.Collections.ObjectModel;
using SecureFileExplorer.Client.Services;
using SecureFileExplorer.Contracts;

namespace SecureFileExplorer.Client.ViewModels;

/// <summary>
/// フォルダーツリーのノード。展開時に子フォルダーを遅延読み込みする。
/// </summary>
public sealed class FolderViewModel : ViewModelBase
{
    private static readonly FolderViewModel Placeholder = new();

    private readonly ApiClient? _api;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _childrenLoaded;

    public long Id { get; }
    public string Name { get; }
    public bool HasChildren { get; }

    public ObservableCollection<FolderViewModel> Children { get; } = new();

    private FolderViewModel() // placeholder 用
    {
        Name = string.Empty;
    }

    public FolderViewModel(FolderDto dto, ApiClient api)
    {
        _api = api;
        Id = dto.Id;
        Name = dto.Name;
        HasChildren = dto.HasChildren;
        if (HasChildren) Children.Add(Placeholder); // 展開矢印を出すためのダミー
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value) && value)
                _ = LoadChildrenAsync();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public async Task LoadChildrenAsync()
    {
        if (_childrenLoaded || _api is null) return;
        _childrenLoaded = true;

        Children.Clear(); // placeholder 除去
        try
        {
            var contents = await _api.GetContentsAsync(Id);
            if (contents is null) return;
            foreach (var f in contents.Folders)
                Children.Add(new FolderViewModel(f, _api));
        }
        catch
        {
            _childrenLoaded = false; // 失敗時は再試行できるように
        }
    }
}
