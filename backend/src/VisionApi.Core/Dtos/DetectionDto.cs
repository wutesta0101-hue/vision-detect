using VisionApi.Core.Entities;

namespace VisionApi.Core.Dtos;

// 對外的辨識紀錄格式。
//
// 為什麼要抽出來：Controller 的 HTTP 回應與 SignalR 的推播必須是同一個結構，
// 否則前端要處理兩種格式。集中在這裡，兩邊都用 FromEntity 轉換。
//
// 不直接回傳 Entity 的理由：避免資料庫結構洩漏給客戶端，
// 也避免 EF 的導覽屬性造成 JSON 循環參照。
public record DetectionDto(
    Guid Id,
    Guid RequestId,
    string Status,
    DateTime CapturedAt,
    DateTime ReceivedAt,
    DateTime? CompletedAt,
    string DeviceId,
    string ModelVersion,
    int InferenceMs,
    int ImageWidth,
    int ImageHeight,
    int AttemptCount,
    string? FailureReason,
    IReadOnlyList<DetectionBoxDto> Detections)
{
    // 實體轉 DTO。時間標記為 UTC，客戶端會收到帶 Z 的 ISO 8601 字串。
    public static DetectionDto FromEntity(DetectionRecord r) => new(
        r.Id,
        r.RequestId,
        r.Status.ToString(),
        DateTime.SpecifyKind(r.CapturedAt, DateTimeKind.Utc),
        DateTime.SpecifyKind(r.ReceivedAt, DateTimeKind.Utc),
        r.CompletedAt.HasValue
            ? DateTime.SpecifyKind(r.CompletedAt.Value, DateTimeKind.Utc)
            : null,
        r.DeviceId,
        r.ModelVersion,
        r.InferenceMs,
        r.ImageWidth,
        r.ImageHeight,
        r.AttemptCount,
        r.FailureReason,
        r.Objects.Select(DetectionBoxDto.FromEntity).ToList());
}

// 單一偵測框。座標為原圖像素、左上角 + 寬高。
public record DetectionBoxDto(
    string Label,
    int ClassId,
    double Confidence,
    int X,
    int Y,
    int Width,
    int Height)
{
    public static DetectionBoxDto FromEntity(DetectedObject o) => new(
        o.Label, o.ClassId, o.Confidence, o.X, o.Y, o.Width, o.Height);
}
