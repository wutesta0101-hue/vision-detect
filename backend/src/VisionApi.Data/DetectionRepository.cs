using Microsoft.EntityFrameworkCore;
using VisionApi.Core.Abstractions;
using VisionApi.Core.Entities;

namespace VisionApi.Data;

// IDetectionRepository 的 EF Core 實作。
// 所有 SQL 相關的細節都收在這裡，service 與 controller 不需要知道。
public class DetectionRepository : IDetectionRepository
{
    private readonly VisionDbContext _db;

    public DetectionRepository(VisionDbContext db) => _db = db;

    // 新增一筆紀錄（含其偵測框）
    public async Task<DetectionRecord> AddAsync(DetectionRecord record, CancellationToken ct = default)
    {
        _db.Records.Add(record);
        await _db.SaveChangesAsync(ct);
        return record;
    }

    // 依 ID 查詢。Include 明確載入偵測框，避免存取時才發現沒載到。
    public Task<DetectionRecord?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Records
            .Include(r => r.Objects)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    // 依去重鍵查詢。冪等性檢查用。
    public Task<DetectionRecord?> GetByRequestIdAsync(Guid requestId, CancellationToken ct = default) =>
        _db.Records
            .Include(r => r.Objects)
            .FirstOrDefaultAsync(r => r.RequestId == requestId, ct);

    // 儲存修改。EF 的變更追蹤會自動判斷哪些欄位變了。
    public Task UpdateAsync(DetectionRecord record, CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);

    // 歷史列表。
    // AsNoTracking：純讀取不需要變更追蹤，省記憶體也快一些。
    public async Task<IReadOnlyList<DetectionRecord>> ListAsync(
        int skip = 0, int take = 50, CancellationToken ct = default)
    {
        return await _db.Records
            .AsNoTracking()
            .Include(r => r.Objects)
            .OrderByDescending(r => r.ReceivedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }
}
