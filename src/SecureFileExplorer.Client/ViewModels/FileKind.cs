namespace SecureFileExplorer.Client.ViewModels;

/// <summary>ファイル種別（色・アイコンの出し分けに使う）。</summary>
public enum FileKind
{
    Folder,
    Excel,
    Word,
    Pdf,
    Ppt,
    Image,
    Zip,
    Other,
}

public static class FileKindResolver
{
    public static FileKind FromExtension(string extension, bool isFolder)
    {
        if (isFolder) return FileKind.Folder;
        return extension.ToLowerInvariant() switch
        {
            ".xlsx" or ".xls" or ".xlsm" or ".xlsb" or ".csv" => FileKind.Excel,
            ".docx" or ".doc" or ".docm" or ".rtf" => FileKind.Word,
            ".pdf" => FileKind.Pdf,
            ".pptx" or ".ppt" or ".pptm" => FileKind.Ppt,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".webp" => FileKind.Image,
            ".zip" or ".7z" or ".rar" or ".lzh" or ".tar" or ".gz" => FileKind.Zip,
            _ => FileKind.Other,
        };
    }
}
