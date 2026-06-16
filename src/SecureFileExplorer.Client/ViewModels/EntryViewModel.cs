using SecureFileExplorer.Contracts;

namespace SecureFileExplorer.Client.ViewModels;

/// <summary>
/// 右ペインの1行。フォルダーまたはファイルのどちらかを表す（Explorer風に両方を並べる）。
/// 色・アイコンは Kind を元に XAML 側（Colors.xaml / Converter / DataTrigger）で解決する。
/// </summary>
public sealed class EntryViewModel
{
    public bool IsFolder { get; }
    public long Id { get; }
    public string Name { get; }
    public FileKind Kind { get; }

    public FileDto? File { get; }
    public FolderDto? Folder { get; }

    public EntryViewModel(FolderDto folder)
    {
        IsFolder = true;
        Id = folder.Id;
        Name = folder.Name;
        Folder = folder;
        Kind = FileKind.Folder;
    }

    public EntryViewModel(FileDto file)
    {
        IsFolder = false;
        Id = file.Id;
        Name = file.Name;
        File = file;
        Kind = FileKindResolver.FromExtension(file.Extension, isFolder: false);
    }

    public string TypeLabel => IsFolder
        ? "フォルダー"
        : (string.IsNullOrEmpty(File!.Extension) ? "ファイル" : File.Extension.TrimStart('.').ToUpperInvariant());

    public string SizeLabel => IsFolder ? string.Empty : FormatSize(File!.SizeBytes);

    public string ModifiedLabel => IsFolder
        ? string.Empty
        : File!.ModifiedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm");

    /// <summary>下部の情報表示用テキスト。</summary>
    public string InfoLabel => IsFolder
        ? $"{Name}   |   種類: フォルダー"
        : $"{Name}   |   種類: {TypeLabel}   |   サイズ: {SizeLabel}   |   更新: {ModifiedLabel}";

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{bytes} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }
}
