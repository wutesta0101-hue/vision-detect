using System.Text.Json.Serialization;

namespace VisionCapture.Models;

// 與後端往來的資料結構。
//
// 後端回傳 camelCase（ASP.NET 預設），這裡的屬性是 PascalCase，
// 靠 JsonSerializerOptions 的 PropertyNameCaseInsensitive 對應。
//
// 對照 backend/src/VisionApi.Core/Dtos/DetectionDto.cs。

// POST /api/v1/detections 的回應（202 時只有這兩個欄位）
public class SubmitResponse
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = "Pending";
}

// 完整的辨識紀錄。透過 SignalR 推播或 GET /{jobId} 取得。
public class DetectionResult
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public string Status { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
    public string ModelVersion { get; set; } = "";
    public int InferenceMs { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public int AttemptCount { get; set; }
    public string? FailureReason { get; set; }
    public List<DetectionBox> Detections { get; set; } = new();

    // 是否已進入終態。UI 靠它決定要不要停止等待。
    [JsonIgnore]
    public bool IsFinished => Status is "Done" or "Failed";
}

// 單一偵測框。座標為原圖像素、左上角 + 寬高。
public class DetectionBox
{
    public string Label { get; set; } = "";
    public int ClassId { get; set; }
    public double Confidence { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

// 待上傳的拍攝紀錄。
//
// 離線時暫存在本機，恢復連線後補傳。
// RequestId 在拍照當下就產生 —— 重送時帶同一個值，
// 伺服器靠它去重，不會因為重試而產生兩筆紀錄。
public class PendingCapture
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public string FilePath { get; set; } = "";
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}
