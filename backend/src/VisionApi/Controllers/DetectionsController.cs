using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VisionApi.Core.Abstractions;
using VisionApi.Core.Dtos;
using VisionApi.Core.Entities;
using VisionApi.Core.Enums;

namespace VisionApi.Controllers;

// 辨識端點。
//
// 第三刀起改為非同步：POST 不等待推論，只把作業排進佇列就返回 202。
// 實際推論由 InferenceWorker 在背景進行，結果透過 SignalR 推播，
// 客戶端也可用 jobId 主動查詢（作為推播不可用時的備援）。
//
// 為什麼要這樣：推論要一到兩秒，同步等待會佔住 HTTP 連線，
// 併發一高就會耗盡連線數而崩潰。
[ApiController]
[Route("api/v1/[controller]")]
public class DetectionsController : ControllerBase
{
    private readonly IModelServiceClient _modelService;
    private readonly IDetectionRepository _repository;
    private readonly IImageStorage _storage;
    private readonly IJobQueue _queue;

    public DetectionsController(
        IModelServiceClient modelService,
        IDetectionRepository repository,
        IImageStorage storage,
        IJobQueue queue)
    {
        _modelService = modelService;
        _repository = repository;
        _storage = storage;
        _queue = queue;
    }

    // POST /api/v1/detections —— 接收上傳，建立作業，立即返回
    //
    // 回應：
    //   202 Accepted → 新作業已建立
    //   200 OK       → 這個 requestId 先前送過，回傳原本的作業
    //
    // capturedAt 接受帶時區的 ISO 8601 字串，存進資料庫前轉成 UTC。
    [HttpPost]
    public async Task<IActionResult> Submit(
        IFormFile image,
        [FromForm] Guid? requestId,
        [FromForm] string? deviceId,
        [FromForm] DateTimeOffset? capturedAt,
        CancellationToken ct)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { error = "missing_image", detail = "請提供影像檔案" });

        // requestId 由客戶端產生。沒帶的話伺服器補一個，
        // 但那樣就失去去重能力 —— 行動端一定要帶。
        var key = requestId ?? Guid.NewGuid();

        // 冪等性檢查（第一道）：先查有沒有處理過
        var existing = await _repository.GetByRequestIdAsync(key, ct);
        if (existing is not null) return Ok(DetectionDto.FromEntity(existing));

        // 先存檔再建立作業：影像必須在工作者取用前就已落地
        string imagePath;
        await using (var stream = image.OpenReadStream())
            imagePath = await _storage.SaveAsync(stream, image.FileName, ct);

        var record = new DetectionRecord
        {
            RequestId = key,
            CapturedAt = capturedAt?.UtcDateTime ?? DateTime.UtcNow,
            ReceivedAt = DateTime.UtcNow,
            DeviceId = deviceId ?? "unknown",
            ImagePath = imagePath,
            Status = JobStatus.Pending
        };

        try
        {
            await _repository.AddAsync(record, ct);
        }
        catch (DbUpdateException)
        {
            // 冪等性檢查（第二道）：併發時兩個請求可能都通過上面的查詢，
            // 由資料庫的唯一索引擋下其中一個。這裡把既有的那筆回傳。
            var concurrent = await _repository.GetByRequestIdAsync(key, ct);
            if (concurrent is not null) return Ok(DetectionDto.FromEntity(concurrent));
            throw;
        }

        await _queue.EnqueueAsync(record.Id, ct);

        return Accepted(new { jobId = record.Id, status = record.Status.ToString() });
    }

    // GET /api/v1/detections/{id} —— 查詢作業狀態與結果
    // 作為 SignalR 推播不可用時的備援
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var record = await _repository.GetByIdAsync(id, ct);
        return record is null ? NotFound() : Ok(DetectionDto.FromEntity(record));
    }

    // GET /api/v1/detections —— 歷史列表，供儀表板使用
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);   // 避免一次撈太多把記憶體吃光
        var records = await _repository.ListAsync(skip, take, ct);
        return Ok(records.Select(DetectionDto.FromEntity));
    }

    // GET /api/v1/detections/labels —— 目前模型的類別清單，供前端篩選器使用
    [HttpGet("labels")]
    public async Task<IActionResult> Labels(CancellationToken ct)
    {
        var labels = await _modelService.GetLabelsAsync(ct);
        return Ok(labels);
    }
}
