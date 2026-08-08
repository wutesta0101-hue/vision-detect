using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using VisionCapture.Services;
using VisionCapture.Views;

namespace VisionCapture;

// 應用進入點與相依注入設定。
//
// 三個服務都註冊為 Singleton：
//   ApiService          —— 無狀態，共用一個 HttpClient 比較有效率
//   OfflineQueueService —— 佇列必須全應用共用，否則各畫面看到不同內容
//   HubService          —— SignalR 連線只需要一條
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            // 註冊 CommunityToolkit 的相機控制項。
            // 🔴 少了這一行，CameraView 會在執行期找不到 Handler 而崩潰。
            .UseMauiCommunityToolkitCamera()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // 服務
        builder.Services.AddSingleton<ApiService>();
        builder.Services.AddSingleton<OfflineQueueService>();
        builder.Services.AddSingleton<HubService>();

        // 頁面
        builder.Services.AddSingleton<CameraPage>();
        builder.Services.AddSingleton<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
