using VisionApi.Core.Entities;

namespace VisionApi.Core.Abstractions;

// 辨識紀錄的存取抽象。
// 上層只知道「存一筆」「查一筆」「列出最近的」，不知道底下是 EF Core 還是別的。
public interface IDetectionRepository
{
    Task<DetectionRecord> AddAsync(DetectionRecord record, CancellationToken ct = default);

    Task<DetectionRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // 依接收時間新到舊排序，供儀表板的歷史列表使用
    Task<IReadOnlyList<DetectionRecord>> ListAsync(
        int skip = 0, int take = 50, CancellationToken ct = default);
}

// 影像檔案的存取抽象。
//
// 影像不存進資料庫：BLOB 會讓 DB 迅速膨脹、備份變慢、查詢效能下降。
// 正確做法是檔案存檔案系統，DB 只存路徑。
public interface IImageStorage
{
    // 存檔並回傳相對路徑（例如 "2026/08/06/abc123.jpg"）
    Task<string> SaveAsync(Stream image, string fileName, CancellationToken ct = default);

    // 依相對路徑讀回檔案，找不到時回 null
    Task<Stream?> OpenAsync(string relativePath, CancellationToken ct = default);
}
