using VisionApi.Core.Models;

namespace VisionApi.Core.Abstractions;

// 模型服務的抽象。
//
// 為什麼要介面：
//   1. 測試時注入假實作，不需要真的啟動 Python 服務
//   2. 未來若改用其他推論方式（例如本機 ONNX 降級路徑），換一個實作即可
//
// 業務層只認識這個介面，不知道背後是 HTTP 還是別的東西。
public interface IModelServiceClient
{
    // 對一張影像做推論
    Task<InferenceResult> InferAsync(
        Stream image,
        string fileName,
        double? confThreshold = null,
        CancellationToken ct = default);

    // 取得目前模型的類別清單，供前端篩選器使用
    Task<LabelsResult> GetLabelsAsync(CancellationToken ct = default);
}

// 模型服務回傳錯誤時拋出。
//
// Retryable 是重點：它讓上層知道「重試有沒有意義」。
// 影像損毀（400）重試一百次也不會成功；模型還在載入（503）等一下就好。
public class ModelServiceException : Exception
{
    public string ErrorCode { get; }
    public bool Retryable { get; }

    public ModelServiceException(string errorCode, string detail, bool retryable)
        : base($"{errorCode}: {detail}")
    {
        ErrorCode = errorCode;
        Retryable = retryable;
    }
}
