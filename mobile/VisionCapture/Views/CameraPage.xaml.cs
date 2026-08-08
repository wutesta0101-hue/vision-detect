using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using VisionCapture.Controls;
using VisionCapture.Models;
using VisionCapture.Services;

namespace VisionCapture.Views;

// 拍攝主畫面。
//
// 流程：
//   即時預覽 → 按拍攝 → CameraView 取像 → 存進離線佇列 → 嘗試上傳
//        → 等待 SignalR 推播結果（收不到就輪詢備援）
//
// 為什麼不等上傳完成才顯示：上傳只是把作業排入佇列，
// 推論還要一兩秒。畫面先顯示「處理中」，結果到了再更新。
//
// ⚠️ 與前一版的差異：
//    原本呼叫 MediaPicker 跳到系統相機，現在用 CameraView 在 app 內取像。
//    好處是準心真正發揮作用（對準當下的畫面），而且不會離開 app，
//    連續拍攝時體驗流暢很多。
public partial class CameraPage : ContentPage
{
    private readonly ApiService _api;
    private readonly OfflineQueueService _queue;
    private readonly HubService _hub;

    // 目前等待結果的作業。收到推播時比對是不是這一筆。
    private Guid? _waitingJobId;

    public CameraPage(ApiService api, OfflineQueueService queue, HubService hub)
    {
        InitializeComponent();

        _api = api;
        _queue = queue;
        _hub = hub;

        CrosshairView.Drawable = new CrosshairDrawable();
        _hub.ResultReceived += OnResultReceived;
    }

    // 畫面出現時：請求相機權限、連線、補傳離線佇列
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await EnsureCameraPermissionAsync();

        await _hub.ConnectAsync();
        UpdateStatus();

        // 補傳先前失敗的拍攝
        var sent = await _queue.FlushAsync();
        if (sent > 0) UpdateStatus();
    }

    // ---------- 權限 ----------

    // 相機權限。
    //
    // Android 6 以上必須在執行期請求，只在 manifest 宣告是不夠的。
    // CameraView 沒有權限就無法顯示預覽，所以在畫面出現時就先要。
    private async Task<bool> EnsureCameraPermissionAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status == PermissionStatus.Granted) return true;

        status = await Permissions.RequestAsync<Permissions.Camera>();
        if (status == PermissionStatus.Granted) return true;

        await DisplayAlert("需要相機權限", "請在系統設定中允許相機存取。", "確定");
        return false;
    }

    // ---------- 拍攝 ----------

    // 按下拍攝。實際的影像會在 OnMediaCaptured 事件中取得。
    private async void OnCaptureClicked(object sender, EventArgs e)
    {
        if (!await EnsureCameraPermissionAsync()) return;

        if (!Camera.IsAvailable)
        {
            await DisplayAlert("相機不可用", "無法存取相機，請確認權限與裝置狀態。", "確定");
            return;
        }

        SetBusy(true);

        try
        {
            // 取像本身很快，逾時設 10 秒足夠
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await Camera.CaptureImage(cts.Token);
        }
        catch (Exception ex)
        {
            SetBusy(false);
            ShowResult("拍攝失敗", ex.Message, "");
        }
    }

    // 取像完成。事件可能在背景執行緒觸發，寫檔與 UI 更新都要注意。
    private async void OnMediaCaptured(object? sender, MediaCapturedEventArgs e)
    {
        // 先把串流寫成暫存檔 —— 後續的離線佇列與上傳都以檔案為單位
        var filePath = Path.Combine(
            FileSystem.CacheDirectory, $"capture_{DateTime.UtcNow:yyyyMMddHHmmssfff}.jpg");

        await using (var file = File.Create(filePath))
            await e.Media.CopyToAsync(file);

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            HintLabel.IsVisible = false;
            await SubmitAsync(filePath);
        });
    }

    // 取像失敗（例如硬體忙碌、權限被撤銷）
    private void OnMediaCaptureFailed(object? sender, MediaCaptureFailedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SetBusy(false);
            ShowResult("拍攝失敗", e.FailureReason, "");
        });
    }

    // 上傳並等待結果
    private async Task SubmitAsync(string filePath)
    {
        ShowResult("處理中", "影像已送出，等待辨識結果…", "");

        var response = await _queue.SubmitAsync(filePath);
        UpdateStatus();

        if (response is null)
        {
            // 上傳失敗，但影像已存在離線佇列，回到有網路時會自動補傳
            ShowResult("已暫存", "目前無法連線，恢復網路後會自動上傳。", "");
            SetBusy(false);
            return;
        }

        _waitingJobId = response.JobId;

        // SignalR 沒收到推播時的備援：輪詢查詢。
        // 兩者哪個先到就用哪個。
        _ = PollFallbackAsync(response.JobId);
    }

    // ---------- 接收結果 ----------

    // SignalR 推播。注意它在背景執行緒觸發，更新 UI 要切回主執行緒。
    private void OnResultReceived(DetectionResult result)
    {
        if (result.Id != _waitingJobId) return;   // 不是我送的那筆（可能是別台裝置的）

        MainThread.BeginInvokeOnMainThread(() => ApplyResult(result));
    }

    // 輪詢備援。SignalR 連不上或推播遺失時，靠這個取得結果。
    private async Task PollFallbackAsync(Guid jobId)
    {
        for (var i = 0; i < 20; i++)          // 最多等 40 秒
        {
            await Task.Delay(2000);

            if (_waitingJobId != jobId) return;   // 已經有結果了，或使用者又拍了新的

            var result = await _api.GetResultAsync(jobId);
            if (result?.IsFinished != true) continue;

            MainThread.BeginInvokeOnMainThread(() => ApplyResult(result));
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            ShowResult("逾時", "等待結果過久，請稍後於桌機端查看。", "");
            SetBusy(false);
        });
    }

    // 把結果呈現到畫面上
    private void ApplyResult(DetectionResult result)
    {
        _waitingJobId = null;
        SetBusy(false);

        if (result.Status == "Failed")
        {
            ShowResult("辨識失敗", result.FailureReason ?? "未知原因", "");
            return;
        }

        if (result.Detections.Count == 0)
        {
            ShowResult("辨識完成", "畫面中沒有可辨識的物件。",
                       $"{result.InferenceMs} ms · {result.ModelVersion}");
            return;
        }

        // 依類別彙整成「person ×3」的形式，比列出十幾個框易讀
        var summary = result.Detections
            .GroupBy(d => d.Label)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} ×{g.Count()}");

        ShowResult(
            "辨識完成",
            string.Join("　", summary),
            $"{result.Detections.Count} 個物件 · {result.InferenceMs} ms · {result.ModelVersion}");
    }

    // ---------- UI 輔助 ----------

    private void ShowResult(string title, string body, string meta)
    {
        ResultCard.IsVisible = true;
        ResultTitle.Text = title;
        ResultBody.Text = body;
        ResultMeta.Text = meta;
        ResultMeta.IsVisible = !string.IsNullOrEmpty(meta);
    }

    private void SetBusy(bool busy)
    {
        CaptureButton.IsEnabled = !busy;
        CaptureButton.Text = busy ? "處理中…" : "拍攝辨識";
    }

    // 更新連線狀態與待傳數量。
    //
    // 連線指示器的用意：推播斷線時畫面看起來完全正常，
    // 只是不再更新 —— 沒有這個指示，使用者會以為系統沒事。
    private void UpdateStatus()
    {
        var connected = _hub.IsConnected;

        ConnectionDot.Fill = connected
            ? new SolidColorBrush(Color.FromArgb("#34C759"))
            : new SolidColorBrush(Color.FromArgb("#FF3B30"));

        StatusLabel.Text = connected
            ? $"已連線 · {ApiService.DeviceId}"
            : "未連線（結果將以輪詢取得）";

        var pending = _queue.PendingCount;
        PendingLabel.Text = pending > 0 ? $"待傳 {pending}" : "";
    }
}
