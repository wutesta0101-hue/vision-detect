using Microsoft.AspNetCore.SignalR;
using VisionApi.Core.Abstractions;
using VisionApi.Core.Dtos;
using VisionApi.Core.Entities;
using VisionApi.Core.Enums;
using VisionApi.Core.Jobs;
using VisionApi.Hubs;

namespace VisionApi.BackgroundServices;

// 背景工作者：從佇列取出作業，呼叫模型服務，更新紀錄狀態，推播結果。
//
// 這是非同步設計的核心。POST 只負責把作業排進佇列就返回，
// 實際的推論在這裡發生 —— HTTP 連線不會被佔住一兩秒。
//
// 狀態流轉：Pending → Processing → Done | Failed
//                          └──→ Pending（可重試的失敗，放回佇列）
// 每次轉移都經過 JobStateMachine 檢查，非法轉移會被擋下並記錄。
//
// 兩層重試的分工：
//   HTTP 層（Polly）—— 單次呼叫內的瞬間故障，秒級，作業無感
//   作業層（本類別）—— 服務較長時間不可用，分鐘級，放回佇列稍後再試
public class InferenceWorker : BackgroundService
{
    private readonly IJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<DetectionHub> _hub;
    private readonly ILogger<InferenceWorker> _logger;
    private readonly int _maxAttempts;
    private readonly int _requeueDelaySeconds;

    public InferenceWorker(
        IJobQueue queue,
        IServiceScopeFactory scopeFactory,
        IHubContext<DetectionHub> hub,
        IConfiguration config,
        ILogger<InferenceWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _hub = hub;
        _logger = logger;
        _maxAttempts = config.GetValue("Worker:MaxAttempts", 3);
        _requeueDelaySeconds = config.GetValue("Worker:RequeueDelaySeconds", 20);
    }

    // 主迴圈：持續取出作業處理，直到應用關閉。
    //
    // 整個迴圈包在 try/catch 裡：單一作業失敗不能讓工作者停擺，
    // 否則後面所有作業都會卡住。
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InferenceWorker 已啟動");

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid jobId;
            try
            {
                jobId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;   // 應用正在關閉，正常結束
            }

            try
            {
                await ProcessAsync(jobId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "作業 {JobId} 處理時發生未預期錯誤", jobId);
            }
        }

        _logger.LogInformation("InferenceWorker 已停止");
    }

    // 處理單一作業。
    //
    // 背景服務是 Singleton，但 Repository 是 Scoped，
    // 所以每個作業要自己開一個 scope 取得相依物件。
    private async Task ProcessAsync(Guid jobId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDetectionRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<IImageStorage>();
        var modelService = scope.ServiceProvider.GetRequiredService<IModelServiceClient>();

        var record = await repository.GetByIdAsync(jobId, ct);
        if (record is null)
        {
            _logger.LogWarning("找不到作業 {JobId}，可能已被刪除", jobId);
            return;
        }

        // 終態的作業不再處理。
        // 防止重複入列造成結果被覆寫。
        if (JobStateMachine.IsTerminal(record.Status))
        {
            _logger.LogWarning("作業 {JobId} 已是終態 {Status}，略過", jobId, record.Status);
            return;
        }

        if (!TryTransition(record, JobStatus.Processing)) return;
        record.AttemptCount++;
        await repository.UpdateAsync(record, ct);

        try
        {
            var stream = await storage.OpenAsync(record.ImagePath, ct);
            if (stream is null)
            {
                // 檔案不見了 —— 重試也不會回來，直接終結
                await Fail(repository, record, "找不到影像檔案", ct);
                return;
            }

            await using (stream)
            {
                var result = await modelService.InferAsync(stream, record.ImagePath, null, ct);

                record.ModelVersion = result.ModelVersion;
                record.InferenceMs = result.InferenceMs;
                record.ImageWidth = result.ImageWidth;
                record.ImageHeight = result.ImageHeight;
                record.Objects = result.Detections.Select(d => new DetectedObject
                {
                    RecordId = record.Id,
                    Label = d.Label,
                    ClassId = d.ClassId,
                    Confidence = d.Confidence,
                    X = d.X,
                    Y = d.Y,
                    Width = d.Width,
                    Height = d.Height
                }).ToList();
            }

            if (!TryTransition(record, JobStatus.Done)) return;
            record.CompletedAt = DateTime.UtcNow;
            await repository.UpdateAsync(record, ct);

            _logger.LogInformation(
                "作業 {JobId} 完成（第 {Attempt} 次嘗試），{Count} 個偵測框，耗時 {Ms}ms",
                jobId, record.AttemptCount, record.Objects.Count, record.InferenceMs);

            await Push("DetectionCompleted", record);
        }
        catch (ModelServiceException ex)
        {
            // 模型服務有回應，但回的是錯誤。
            // Retryable 旗標決定要放回佇列還是直接終結 —— 
            // 這個欄位從第一刀就存在，到這一刀才真正發揮作用。
            await HandleFailure(repository, record, ex.Message, ex.Retryable, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 應用正在關閉。作業留在 Processing，重啟後可由人工或補償機制處理。
            _logger.LogWarning("作業 {JobId} 因應用關閉而中斷", jobId);
            throw;
        }
        catch (Exception ex)
        {
            // 服務連不上、Polly 重試耗盡、斷路器開啟等。
            // 這類都屬於「服務暫時不可用」，視為可重試。
            await HandleFailure(repository, record, $"{ex.GetType().Name}: {ex.Message}", true, ct);
        }
    }

    // 失敗處理。可重試且未達上限就放回佇列，否則終結。
    private async Task HandleFailure(
        IDetectionRepository repository,
        DetectionRecord record,
        string reason,
        bool retryable,
        CancellationToken ct)
    {
        if (retryable && record.AttemptCount < _maxAttempts)
        {
            await Requeue(repository, record, reason);
            return;
        }

        var detail = retryable
            ? $"重試 {record.AttemptCount} 次後仍失敗：{reason}"
            : reason;

        await Fail(repository, record, detail, ct);
    }

    // 放回佇列稍後再試。
    //
    // 為什麼要延遲：服務剛掛掉時立刻重排只會馬上再失敗一次，
    // 而且會讓工作者空轉。延遲後再入列給服務恢復的時間。
    //
    // ⚠️ 延遲用背景 Task 實作，不會阻塞工作者處理其他作業。
    //    代價是應用重啟時這些等待中的重排會遺失 —— 與行程內佇列同樣的限制。
    private async Task Requeue(
        IDetectionRepository repository, DetectionRecord record, string reason)
    {
        if (!TryTransition(record, JobStatus.Pending)) return;

        record.FailureReason = Truncate($"第 {record.AttemptCount} 次嘗試失敗，稍後重試：{reason}");
        await repository.UpdateAsync(record, CancellationToken.None);

        _logger.LogWarning(
            "作業 {JobId} 第 {Attempt}/{Max} 次失敗，{Delay} 秒後重試：{Reason}",
            record.Id, record.AttemptCount, _maxAttempts, _requeueDelaySeconds, reason);

        var jobId = record.Id;
        var delay = TimeSpan.FromSeconds(_requeueDelaySeconds);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay);
                await _queue.EnqueueAsync(jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "作業 {JobId} 重新入列失敗", jobId);
            }
        });
    }

    // 推播給所有連線的客戶端。
    //
    // 推播失敗不能影響作業本身 —— 結果已經寫進資料庫了，
    // 客戶端仍可用 GET /{jobId} 取得。所以這裡吞掉例外只記 log。
    private async Task Push(string eventName, DetectionRecord record)
    {
        try
        {
            await _hub.Clients.All.SendAsync(eventName, DetectionDto.FromEntity(record));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "推播 {Event} 失敗，作業 {JobId}", eventName, record.Id);
        }
    }

    // 狀態轉移。非法轉移不會拋例外，而是記錄並回傳 false —— 
    // 這種情況代表程式邏輯有問題，但不該讓整個工作者掛掉。
    private bool TryTransition(DetectionRecord record, JobStatus next)
    {
        if (!JobStateMachine.CanTransition(record.Status, next))
        {
            _logger.LogError(
                "作業 {JobId} 的非法狀態轉移：{From} → {To}",
                record.Id, record.Status, next);
            return false;
        }

        record.Status = next;
        return true;
    }

    // 標記為終態失敗。失敗原因會回傳給客戶端，儀表板呈現而非隱藏。
    //
    // 注意這裡用 CancellationToken.None：即使原本的 token 已取消，
    // 也要把失敗狀態寫進資料庫，否則作業會卡在 Processing。
    private async Task Fail(
        IDetectionRepository repository, DetectionRecord record, string reason, CancellationToken ct)
    {
        if (!TryTransition(record, JobStatus.Failed)) return;

        record.FailureReason = Truncate(reason);
        record.CompletedAt = DateTime.UtcNow;
        await repository.UpdateAsync(record, CancellationToken.None);

        _logger.LogWarning("作業 {JobId} 最終失敗：{Reason}", record.Id, reason);

        await Push("DetectionFailed", record);
    }

    // 資料庫欄位限制 500 字，例外訊息可能更長。
    private static string Truncate(string text) =>
        text.Length > 500 ? text[..500] : text;
}
