using Microsoft.AspNetCore.SignalR.Client;
using VisionCapture.Models;

namespace VisionCapture.Services;

// SignalR 連線：接收辨識完成的推播。
//
// 為什麼手機也要接：上傳後回的是 202（作業已排入佇列），
// 結果要一兩秒後才出來。沒有推播就只能輪詢，
// 既耗電又浪費流量。
//
// 桌機儀表板也連同一個 hub —— 所以手機拍完照，
// 桌機和手機會「同時」看到結果。
public class HubService : IAsyncDisposable
{
    private readonly ApiService _api;
    private HubConnection? _connection;

    public HubService(ApiService api) => _api = api;

    // 收到辨識結果時觸發。UI 訂閱這個事件更新畫面。
    public event Action<DetectionResult>? ResultReceived;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    // 建立連線。應用啟動時呼叫。
    public async Task ConnectAsync()
    {
        if (_connection is not null) return;

        _connection = new HubConnectionBuilder()
            .WithUrl($"{_api.BaseUrl}/hub/detections")
            .WithAutomaticReconnect()      // 網路中斷時自動重連
            .Build();

        // 成功與失敗都推播，兩者都要處理 —— 
        // 失敗要讓使用者知道，不能靜靜消失
        _connection.On<DetectionResult>("DetectionCompleted", r => ResultReceived?.Invoke(r));
        _connection.On<DetectionResult>("DetectionFailed", r => ResultReceived?.Invoke(r));

        try
        {
            await _connection.StartAsync();
        }
        catch
        {
            // 連不上不是致命錯誤 —— 仍可用輪詢取得結果。
            // 這也是 ApiService.GetResultAsync 存在的理由。
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
