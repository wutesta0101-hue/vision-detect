import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr'

// SignalR 連線管理。
//
// 為什麼要包一層：連線的建立、重連、事件訂閱散在元件裡很難維護，
// 而且元件卸載時忘記斷線會造成記憶體洩漏。
//
// withAutomaticReconnect：網路短暫中斷時自動重連，
// 不用自己寫重試邏輯。重連期間的推播會遺失，所以重連成功後
// 應該重新載入列表（見 store 的 onReconnected）。

const HUB_URL =
  (import.meta.env.VITE_API_BASE ?? 'http://localhost:5273') + '/hub/detections'

export function createHubConnection() {
  return new HubConnectionBuilder()
    .withUrl(HUB_URL, {
      // 明確指定 WebSocket。不指定的話會先嘗試協商，
      // 反向代理設定不完整時會靜靜退回長輪詢 —— 功能正常但失去即時性。
      transport: HttpTransportType.WebSockets,
      skipNegotiation: true,
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(LogLevel.Warning)
    .build()
}
