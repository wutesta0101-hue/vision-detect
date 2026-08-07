using System.Net;

namespace VisionApi.Infrastructure.ModelService;

// HTTP 狀態碼的重試分類。
//
// 這是整個韌性策略的判斷依據 —— 重試只在「等一下可能會成功」時才有意義。
// 影像損毀重試一百次也不會變成合法影像，只是浪費時間並讓日誌看起來像服務不穩。
//
// 對照 model-service/CONTRACT.md 的狀態碼表。
public static class ModelServiceErrors
{
    public static bool IsRetryable(HttpStatusCode status) => status switch
    {
        // 客戶端問題 —— 重試無用
        HttpStatusCode.BadRequest => false,              // 400 影像損毀、參數非法
        HttpStatusCode.RequestEntityTooLarge => false,   // 413 檔案過大
        HttpStatusCode.NotFound => false,                // 404 端點不存在（設定錯誤）

        // 逾時與伺服器端問題 —— 重試可能成功
        HttpStatusCode.RequestTimeout => true,           // 408
        HttpStatusCode.TooManyRequests => true,          // 429 被限流
        HttpStatusCode.ServiceUnavailable => true,       // 503 模型還在載入
        HttpStatusCode.BadGateway => true,               // 502 代理層問題
        HttpStatusCode.GatewayTimeout => true,           // 504

        // 其餘 5xx 一律視為可重試
        _ => (int)status >= 500
    };
}
