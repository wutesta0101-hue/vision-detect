namespace VisionApi.Core.Models;

// 模型服務回傳的資料結構，對應 model-service/CONTRACT.md。
//
// 屬性名稱用 C# 慣例（PascalCase），JSON 的 snake_case 由序列化選項轉換，
// 轉換設定集中在 ModelServiceClient，其他層不需要知道這件事。
//
// 改動這裡的欄位前，記得同步 CONTRACT.md 與 Python 端的 schemas.py。

// 單一偵測框。座標為原圖像素、左上角 + 寬高。
public record Detection(
    string Label,
    int ClassId,
    double Confidence,
    int X,
    int Y,
    int Width,
    int Height);

// POST /infer 的完整回應
public record InferenceResult(
    string ModelVersion,
    int InferenceMs,
    int ImageWidth,
    int ImageHeight,
    IReadOnlyList<Detection> Detections);

// GET /labels 清單中的單筆
public record LabelItem(int ClassId, string Label);

// GET /labels 的回應
public record LabelsResult(
    string ModelVersion,
    int Count,
    IReadOnlyList<LabelItem> Labels);
