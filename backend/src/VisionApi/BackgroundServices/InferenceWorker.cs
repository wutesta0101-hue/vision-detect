using VisionApi.Core.Abstractions;
using VisionApi.Core.Entities;
using VisionApi.Core.Enums;
using VisionApi.Core.Jobs;

namespace VisionApi.BackgroundServices;

// 背景工作者：從佇列取出作業，呼叫模型服務，更新紀錄狀態。
//
// 這是非同步設計的核心。POST 只負責把作業排進佇列就返回，
// 實際的推論在這裡發生 —— HTTP 連線不會被佔住一兩秒。
//
// 狀態流轉：Pending → Processing → Done | Failed
// 每次轉移都經過 JobStateMachine 檢查，非法轉移會被擋下並記錄。
public class InferenceWorker : BackgroundService
{
    private readonly IJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InferenceWorker> _logger;

    public InferenceWorker(
        IJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<InferenceWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
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
        // 防止重複入列（例如未來加入重試機制時）造成結果被覆寫。
        if (JobStateMachine.IsTerminal(record.Status))
        {
            _logger.LogWarning("作業 {JobId} 已是終態 {Status}，略過", jobId, record.Status);
            return;
        }

        if (!TryTransition(record, JobStatus.Processing)) return;
        await repository.UpdateAsync(record, ct);

        try
        {
            var stream = await storage.OpenAsync(record.ImagePath, ct);
            if (stream is null)
            {
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
                "作業 {JobId} 完成，{Count} 個偵測框，耗時 {Ms}ms",
                jobId, record.Objects.Count, record.InferenceMs);
        }
        catch (ModelServiceException ex)
        {
            // 模型服務有回應，但回的是錯誤（400、503、500 等）。
            // 目前不論可否重試都直接標記失敗。
            // 重試策略在第五刀用 Polly 加上，屆時 Retryable 才會真正發揮作用。
            await Fail(repository, record, ex.Message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 應用正在關閉。作業留在 Processing，重啟後可由人工或補償機制處理。
            // 不標記為 Failed，因為它其實沒有失敗，只是被中斷。
            _logger.LogWarning("作業 {JobId} 因應用關閉而中斷", jobId);
            throw;
        }
        catch (Exception ex)
        {
            // 服務連不上、呼叫逾時、回應解析失敗等未預期狀況。
            //
            // 🔴 這個 catch 不能省：沒有它的話，例外會冒到外層迴圈，
            //    作業就永遠卡在 Processing —— 那比明確失敗更糟，
            //    因為客戶端會一直等一個不會到來的結果。
            await Fail(repository, record, $"{ex.GetType().Name}: {ex.Message}", ct);
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

    // 標記失敗。失敗原因會回傳給客戶端，儀表板呈現而非隱藏。
    //
    // 注意這裡用 CancellationToken.None：即使原本的 token 已取消，
    // 也要把失敗狀態寫進資料庫，否則作業會卡在 Processing。
    private async Task Fail(
        IDetectionRepository repository, DetectionRecord record, string reason, CancellationToken ct)
    {
        if (!TryTransition(record, JobStatus.Failed)) return;

        record.FailureReason = reason.Length > 500 ? reason[..500] : reason;
        record.CompletedAt = DateTime.UtcNow;
        await repository.UpdateAsync(record, CancellationToken.None);

        _logger.LogWarning("作業 {JobId} 失敗：{Reason}", record.Id, reason);
    }
}
