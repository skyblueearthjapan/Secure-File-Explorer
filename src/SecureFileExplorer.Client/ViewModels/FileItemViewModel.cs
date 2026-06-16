using SecureFileExplorer.Contracts;

namespace SecureFileExplorer.Client.ViewModels;

/// <summary>ファイル一覧の1行。表示用に整形した値を持つ。</summary>
public sealed class FileItemViewModel
{
    public FileDto Dto { get; }

    public FileItemViewModel(FileDto dto) => Dto = dto;

    public long Id => Dto.Id;
    public string Name => Dto.Name;
    public string TypeLabel => string.IsNullOrEmpty(Dto.Extension) ? "ファイル" : Dto.Extension.TrimStart('.').ToUpperInvariant();
    public string SizeLabel => FormatSize(Dto.SizeBytes);
    public string ModifiedLabel => Dto.ModifiedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm");

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{bytes} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }
}
