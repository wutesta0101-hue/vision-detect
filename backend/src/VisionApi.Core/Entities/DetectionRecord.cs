using VisionApi.Core.Enums;

namespace VisionApi.Core.Entities;

// 一次辨識作業的紀錄。對應資料表 detection_records。
//
// 為什麼有兩個時間戳：
//   CapturedAt 是手機拍照的時刻，ReceivedAt 是伺服器收到的時刻。
//   手機離線暫存後補傳時，兩者可能差很多 —— 儀表板要顯示哪一個
//   取決於使用情境，兩個都存下來才有選擇權。
//
// 為什麼用 DateTime 而非 DateTimeOffset：
//   一律存 UTC，顯示時才轉當地時區。Npgsql 對應 PostgreSQL 的
//   timestamptz 時建議用 Kind=Utc 的 DateTime，SQLite 也支援排序。
//   客戶端仍可送帶時區的 ISO 8601 字串，轉換在 Controller 完成。
public class DetectionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CapturedAt { get; set; }            // 拍攝時間（UTC）
    public DateTime ReceivedAt { get; set; }            // 伺服器接收時間（UTC）

    public string DeviceId { get; set; } = "unknown";   // 哪台裝置送來的
    public string ImagePath { get; set; } = "";         // 影像檔路徑，不存二進位進 DB

    // 換模型後，舊紀錄是舊模型算的。
    // 沒有這個欄位，之後分不清準確度變化是模型改進還是資料不同。
    public string ModelVersion { get; set; } = "";

    public int InferenceMs { get; set; }
    public int ImageWidth { get; set; }                 // 座標的參考基準
    public int ImageHeight { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Done;

    public List<DetectedObject> Objects { get; set; } = new();
}

// 單一偵測框。對應資料表 detected_objects。
// 座標為原圖像素、左上角 + 寬高，與模型服務的契約一致。
public class DetectedObject
{
    public long Id { get; set; }
    public Guid RecordId { get; set; }

    public string Label { get; set; } = "";
    public int ClassId { get; set; }
    public double Confidence { get; set; }

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
