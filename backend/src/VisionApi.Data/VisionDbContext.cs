using Microsoft.EntityFrameworkCore;
using VisionApi.Core.Entities;

namespace VisionApi.Data;

// EF Core 的資料庫對應設定。
//
// 命名慣例：C# 用 PascalCase，PostgreSQL 慣用 snake_case。
// 這裡明確指定資料表與欄位名稱，兩邊各自保持自己的慣例。
public class VisionDbContext : DbContext
{
    public VisionDbContext(DbContextOptions<VisionDbContext> options) : base(options) { }

    public DbSet<DetectionRecord> Records => Set<DetectionRecord>();
    public DbSet<DetectedObject> Objects => Set<DetectedObject>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<DetectionRecord>(e =>
        {
            e.ToTable("detection_records");
            e.HasKey(x => x.Id);

            e.Property(x => x.DeviceId).HasMaxLength(100).IsRequired();
            e.Property(x => x.ImagePath).HasMaxLength(500).IsRequired();
            e.Property(x => x.ModelVersion).HasMaxLength(100).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FailureReason).HasMaxLength(500);

            // 去重鍵的唯一索引。
            //
            // 這是冪等性的最後一道防線：兩個執行緒同時檢查「這個 RequestId
            // 存在嗎」都得到「不存在」時，程式碼層的檢查會失效，
            // 但資料庫的唯一約束會讓其中一個插入失敗。
            e.HasIndex(x => x.RequestId).IsUnique();

            // 歷史列表依接收時間排序，加索引避免資料量大時全表掃描
            e.HasIndex(x => x.ReceivedAt);

            // 依狀態篩選（例如只看失敗的）是儀表板的常用查詢
            e.HasIndex(x => x.Status);

            // 刪除紀錄時連同偵測框一起刪，不留孤兒資料
            e.HasMany(x => x.Objects)
                .WithOne()
                .HasForeignKey(x => x.RecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DetectedObject>(e =>
        {
            e.ToTable("detected_objects");
            e.HasKey(x => x.Id);

            e.Property(x => x.Label).HasMaxLength(100).IsRequired();

            // 依類別篩選是儀表板的常用查詢
            e.HasIndex(x => x.Label);
        });
    }
}
