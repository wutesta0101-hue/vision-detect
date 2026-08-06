using Microsoft.EntityFrameworkCore;
using VisionApi.Core.Entities;
using VisionApi.Core.Enums;
using VisionApi.Data;
using Xunit;

namespace VisionApi.Data.Tests;

// 資料存取測試。
//
// 用記憶體 SQLite 而非真實 PostgreSQL：
//   跑得快、不需要 Docker、每個測試拿到全新的空資料庫。
//   代價是兩者的 SQL 方言有差異，所以真實連線另外用手動驗證確認。
public class DetectionRepositoryTests : IDisposable
{
    private readonly VisionDbContext _db;
    private readonly DetectionRepository _repo;

    public DetectionRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<VisionDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _db = new VisionDbContext(options);
        _db.Database.OpenConnection();      // 記憶體資料庫在連線關閉時消失，要手動開著
        _db.Database.EnsureCreated();

        _repo = new DetectionRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    // 產生一筆測試資料。時間一律用 UTC，與實體的約定一致。
    private static DetectionRecord MakeRecord(
        string device = "phone-1",
        int objectCount = 2,
        Guid? requestId = null,
        JobStatus status = JobStatus.Done) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        CapturedAt = DateTime.UtcNow.AddMinutes(-5),
        ReceivedAt = DateTime.UtcNow,
        DeviceId = device,
        ImagePath = "2026/08/06/test.jpg",
        ModelVersion = "yolov8n-coco",
        InferenceMs = 120,
        ImageWidth = 1920,
        ImageHeight = 1080,
        Status = status,
        Objects = Enumerable.Range(0, objectCount).Select(i => new DetectedObject
        {
            Label = "person",
            ClassId = 0,
            Confidence = 0.9,
            X = i * 10,
            Y = 20,
            Width = 30,
            Height = 40
        }).ToList()
    };

    // ---------- 寫入 ----------

    [Fact]
    public async Task 新增紀錄應產生ID()
    {
        var record = await _repo.AddAsync(MakeRecord());
        Assert.NotEqual(Guid.Empty, record.Id);
    }

    [Fact]
    public async Task 偵測框應一併寫入()
    {
        var record = await _repo.AddAsync(MakeRecord(objectCount: 3));

        var fetched = await _repo.GetByIdAsync(record.Id);

        Assert.NotNull(fetched);
        Assert.Equal(3, fetched!.Objects.Count);
    }

    [Fact]
    public async Task 無偵測框的紀錄也應能存入()
    {
        // 空畫面是合法情境，不該因為沒有偵測框就失敗
        var record = await _repo.AddAsync(MakeRecord(objectCount: 0));

        var fetched = await _repo.GetByIdAsync(record.Id);

        Assert.NotNull(fetched);
        Assert.Empty(fetched!.Objects);
    }

    // ---------- 往返一致性 ----------

    [Fact]
    public async Task 存入再讀出內容應完全一致()
    {
        // 這是最重要的測試 —— 它守住欄位對應不會寫錯
        var original = await _repo.AddAsync(MakeRecord());

        var fetched = await _repo.GetByIdAsync(original.Id);

        Assert.Equal(original.DeviceId, fetched!.DeviceId);
        Assert.Equal(original.ModelVersion, fetched.ModelVersion);
        Assert.Equal(original.ImageWidth, fetched.ImageWidth);
        Assert.Equal(original.ImageHeight, fetched.ImageHeight);
        Assert.Equal(original.InferenceMs, fetched.InferenceMs);
        Assert.Equal(original.Status, fetched.Status);
    }

    [Fact]
    public async Task 偵測框座標應完全一致()
    {
        var original = await _repo.AddAsync(MakeRecord(objectCount: 1));
        var expected = original.Objects[0];

        var actual = (await _repo.GetByIdAsync(original.Id))!.Objects[0];

        Assert.Equal(expected.Label, actual.Label);
        Assert.Equal(expected.ClassId, actual.ClassId);
        Assert.Equal(expected.X, actual.X);
        Assert.Equal(expected.Y, actual.Y);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
    }

    // ---------- 冪等性（第三刀新增）----------

    [Fact]
    public async Task 依RequestId應查得到紀錄()
    {
        var key = Guid.NewGuid();
        await _repo.AddAsync(MakeRecord(requestId: key));

        var fetched = await _repo.GetByRequestIdAsync(key);

        Assert.NotNull(fetched);
        Assert.Equal(key, fetched!.RequestId);
    }

    [Fact]
    public async Task 查詢不存在的RequestId應回傳null()
    {
        Assert.Null(await _repo.GetByRequestIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task 相同RequestId不可存入兩次()
    {
        // 唯一索引是冪等性的最後一道防線。
        // 程式碼層的檢查在併發時可能同時通過，但資料庫不會讓兩筆都進去。
        var key = Guid.NewGuid();
        await _repo.AddAsync(MakeRecord(requestId: key));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => _repo.AddAsync(MakeRecord(requestId: key)));
    }

    // ---------- 狀態更新（第三刀新增）----------

    [Fact]
    public async Task 狀態更新應被儲存()
    {
        var record = await _repo.AddAsync(MakeRecord(status: JobStatus.Pending));

        record.Status = JobStatus.Processing;
        await _repo.UpdateAsync(record);

        var fetched = await _repo.GetByIdAsync(record.Id);
        Assert.Equal(JobStatus.Processing, fetched!.Status);
    }

    [Fact]
    public async Task 失敗原因應被儲存()
    {
        var record = await _repo.AddAsync(MakeRecord(status: JobStatus.Pending));

        record.Status = JobStatus.Failed;
        record.FailureReason = "模型服務逾時";
        record.CompletedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(record);

        var fetched = await _repo.GetByIdAsync(record.Id);
        Assert.Equal(JobStatus.Failed, fetched!.Status);
        Assert.Equal("模型服務逾時", fetched.FailureReason);
        Assert.NotNull(fetched.CompletedAt);
    }

    // ---------- 查詢 ----------

    [Fact]
    public async Task 查詢不存在的ID應回傳null()
    {
        Assert.Null(await _repo.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task 列表應依接收時間新到舊排序()
    {
        var older = MakeRecord("phone-old");
        older.ReceivedAt = DateTime.UtcNow.AddHours(-2);
        await _repo.AddAsync(older);

        var newer = MakeRecord("phone-new");
        newer.ReceivedAt = DateTime.UtcNow;
        await _repo.AddAsync(newer);

        var list = await _repo.ListAsync();

        Assert.Equal("phone-new", list[0].DeviceId);
    }

    [Fact]
    public async Task 分頁參數應生效()
    {
        for (var i = 0; i < 5; i++) await _repo.AddAsync(MakeRecord($"phone-{i}"));

        Assert.Equal(2, (await _repo.ListAsync(skip: 0, take: 2)).Count);
        Assert.Equal(3, (await _repo.ListAsync(skip: 2, take: 10)).Count);
    }

    // ---------- 關聯刪除 ----------

    [Fact]
    public async Task 刪除紀錄應連帶刪除偵測框()
    {
        var record = await _repo.AddAsync(MakeRecord(objectCount: 2));
        Assert.Equal(2, _db.Objects.Count());

        _db.Records.Remove(record);
        await _db.SaveChangesAsync();

        Assert.Empty(_db.Objects);   // 不該留下孤兒資料
    }
}
