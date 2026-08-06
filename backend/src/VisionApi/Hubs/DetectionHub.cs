using Microsoft.AspNetCore.SignalR;

namespace VisionApi.Hubs;

// 辨識結果的即時推播通道。
//
// 客戶端（Vue 儀表板、MAUI 行動端）連上這個 hub 後，
// 背景工作者完成推論時會主動推送結果 —— 不需要輪詢。
//
// 這個類別本身沒有方法：客戶端不呼叫伺服器，只單向接收。
// 實際的推播由 InferenceWorker 透過 IHubContext<DetectionHub> 發出。
//
// 前端訂閱的事件名稱：
//   DetectionCompleted → 推論成功，帶完整紀錄
//   DetectionFailed    → 推論失敗，帶失敗原因
public class DetectionHub : Hub
{
    private readonly ILogger<DetectionHub> _logger;

    public DetectionHub(ILogger<DetectionHub> logger) => _logger = logger;

    // 連線建立時記錄，方便排查「前端收不到推播」的問題
    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("客戶端已連線：{ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("客戶端已斷線：{ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
