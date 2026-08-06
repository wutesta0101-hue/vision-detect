using VisionApi.Core.Abstractions;

namespace VisionApi.Infrastructure.Storage;

// 把影像存在本機檔案系統。
//
// 依日期分目錄（2026/08/06/xxx.jpg）的原因：
//   單一目錄放幾十萬個檔案時，檔案系統的列目錄操作會明顯變慢，
//   備份與清理舊資料也不好處理。
public class LocalFileImageStorage : IImageStorage
{
    private readonly string _root;

    public LocalFileImageStorage(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    // 存檔並回傳相對路徑。檔名用 GUID 避免不同裝置上傳同名檔案時互相覆蓋。
    public async Task<string> SaveAsync(Stream image, string fileName, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension)) extension = ".jpg";

        var folder = DateTime.UtcNow.ToString("yyyy/MM/dd");
        var relativePath = Path.Combine(folder, $"{Guid.NewGuid():N}{extension}");
        var fullPath = Path.Combine(_root, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = File.Create(fullPath);
        await image.CopyToAsync(file, ct);

        // 統一用正斜線，避免 Windows 存的路徑在 Linux 容器讀不到
        return relativePath.Replace('\\', '/');
    }

    // 讀回檔案。找不到回 null，由呼叫端決定要回 404 還是別的。
    public Task<Stream?> OpenAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_root, relativePath);
        Stream? stream = File.Exists(fullPath) ? File.OpenRead(fullPath) : null;
        return Task.FromResult(stream);
    }
}
