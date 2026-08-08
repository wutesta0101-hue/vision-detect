using System.Text.Json;
using VisionCapture.Models;

namespace VisionCapture.Services;

// 離線佇列：網路不通時把拍攝暫存起來，恢復後自動補傳。
//
// 為什麼需要：現場拍照的場景常常訊號不穩。沒有這個機制的話，
// 上傳失敗就等於照片白拍了。
//
// 冪等性的關鍵：RequestId 在拍照當下就產生並存進佇列，
// 補傳時帶的是同一個值 —— 即使某次上傳其實成功了只是回應沒收到，
// 重送也不會在伺服器產生第二筆紀錄。
public class OfflineQueueService
{
    private readonly ApiService _api;
    private readonly string _queueFile;
    private readonly string _imageDir;

    // 避免補傳與新上傳同時進行造成重複
    private readonly SemaphoreSlim _lock = new(1, 1);

    public OfflineQueueService(ApiService api)
    {
        _api = api;
        _queueFile = Path.Combine(FileSystem.AppDataDirectory, "pending.json");
        _imageDir = Path.Combine(FileSystem.AppDataDirectory, "pending_images");
        Directory.CreateDirectory(_imageDir);
    }

    // 目前待傳的數量，顯示在拍攝畫面上
    public int PendingCount => Load().Count;

    // 拍完照後呼叫：先存進佇列，再嘗試上傳。
    //
    // 先存後傳的順序很重要 —— 若順序相反，上傳失敗時照片就遺失了。
    public async Task<SubmitResponse?> SubmitAsync(string sourcePath, CancellationToken ct = default)
    {
        var capture = new PendingCapture
        {
            FilePath = Path.Combine(_imageDir, $"{Guid.NewGuid():N}.jpg"),
            CapturedAt = DateTime.UtcNow
        };

        File.Copy(sourcePath, capture.FilePath, overwrite: true);

        var queue = Load();
        queue.Add(capture);
        Save(queue);

        return await TrySendAsync(capture, ct);
    }

    // 嘗試補傳所有待傳項目。
    // 在應用回到前景、或網路恢復時呼叫。
    public async Task<int> FlushAsync(CancellationToken ct = default)
    {
        if (!await _lock.WaitAsync(0, ct)) return 0;   // 已有補傳在進行，跳過

        try
        {
            var sent = 0;
            foreach (var capture in Load().ToList())
            {
                if (await TrySendAsync(capture, ct) is not null) sent++;
                else break;   // 一失敗就停 —— 多半是網路還沒恢復，繼續試也是白費
            }
            return sent;
        }
        finally
        {
            _lock.Release();
        }
    }

    // 送出單一項目。成功就從佇列移除並刪掉暫存檔。
    private async Task<SubmitResponse?> TrySendAsync(PendingCapture capture, CancellationToken ct)
    {
        try
        {
            var result = await _api.UploadAsync(
                capture.FilePath, capture.RequestId, capture.CapturedAt, ct);

            Remove(capture.RequestId);
            return result;
        }
        catch
        {
            // 上傳失敗就留在佇列裡，下次再試。
            // 不記錄詳細錯誤是因為離線是預期情境，不是異常。
            return null;
        }
    }

    // ---------- 佇列的讀寫 ----------
    //
    // 用 JSON 檔而非資料庫：待傳項目通常只有幾筆，
    // 引入 SQLite 的複雜度不划算。

    private List<PendingCapture> Load()
    {
        if (!File.Exists(_queueFile)) return new();

        try
        {
            var json = File.ReadAllText(_queueFile);
            return JsonSerializer.Deserialize<List<PendingCapture>>(json) ?? new();
        }
        catch
        {
            return new();   // 檔案損毀時從空佇列開始，不讓應用崩潰
        }
    }

    private void Save(List<PendingCapture> queue) =>
        File.WriteAllText(_queueFile, JsonSerializer.Serialize(queue));

    private void Remove(Guid requestId)
    {
        var queue = Load();
        var target = queue.FirstOrDefault(x => x.RequestId == requestId);
        if (target is null) return;

        queue.Remove(target);
        Save(queue);

        if (File.Exists(target.FilePath)) File.Delete(target.FilePath);
    }
}
