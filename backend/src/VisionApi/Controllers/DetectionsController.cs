using Microsoft.AspNetCore.Mvc;
using VisionApi.Core.Abstractions;
using VisionApi.Core.Entities;
using VisionApi.Core.Enums;

namespace VisionApi.Controllers;

// 辨識端點。
//
// ⚠️ 目前仍是同步版本：收到上傳後直接呼叫模型服務並等待結果。
//    第二刀新增的是「存檔 + 寫入資料庫 + 可查詢歷史」。
//    第三刀會改成非同步（回 202 + jobId），屆時 Detect 方法會大改。
[ApiController]
[Route("api/v1/[controller]")]
public class DetectionsController : ControllerBase
{
    private readonly IModelServiceClient _modelService;
    private readonly IDetectionRepository _repository;
    private readonly IImageStorage _storage;

    public DetectionsController(
        IModelServiceClient modelService,
        IDetectionRepository repository,
        IImageStorage storage)
    {
        _modelService = modelService;
        _repository = repository;
        _storage = storage;
    }

    // POST /api/v1/detections —— 上傳影像、辨識、存檔、寫入資料庫
    //
    // capturedAt 接受帶時區的 ISO 8601 字串（客戶端慣例），
    // 存進資料庫前一律轉成 UTC。
    [HttpPost]
    public async Task<IActionResult> Detect(
        IFormFile image,
        [FromForm] string? deviceId,
        [FromForm] DateTimeOffset? capturedAt,
        [FromForm] double? confThreshold,
        CancellationToken ct)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { error = "missing_image", detail = "請提供影像檔案" });

        // 先存檔再推論：即使推論失敗，原始影像也不會遺失，事後可重跑
        string imagePath;
        await using (var stream = image.OpenReadStream())
            imagePath = await _storage.SaveAsync(stream, image.FileName, ct);

        try
        {
            await using var inferStream = image.OpenReadStream();
            var result = await _modelService.InferAsync(inferStream, image.FileName, confThreshold, ct);

            var record = new DetectionRecord
            {
                CapturedAt = capturedAt?.UtcDateTime ?? DateTime.UtcNow,
                ReceivedAt = DateTime.UtcNow,
                DeviceId = deviceId ?? "unknown",
                ImagePath = imagePath,
                ModelVersion = result.ModelVersion,
                InferenceMs = result.InferenceMs,
                ImageWidth = result.ImageWidth,
                ImageHeight = result.ImageHeight,
                Status = JobStatus.Done,
                Objects = result.Detections.Select(d => new DetectedObject
                {
                    Label = d.Label,
                    ClassId = d.ClassId,
                    Confidence = d.Confidence,
                    X = d.X,
                    Y = d.Y,
                    Width = d.Width,
                    Height = d.Height
                }).ToList()
            };

            await _repository.AddAsync(record, ct);
            return Ok(ToDto(record));
        }
        catch (ModelServiceException ex)
        {
            // 不可重試的錯誤代表輸入有問題 → 400；可重試代表服務暫時不可用 → 503
            var status = ex.Retryable ? 503 : 400;
            return StatusCode(status, new { error = ex.ErrorCode, detail = ex.Message });
        }
    }

    // GET /api/v1/detections/{id} —— 查詢單筆
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var record = await _repository.GetByIdAsync(id, ct);
        return record is null ? NotFound() : Ok(ToDto(record));
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
        return Ok(records.Select(ToDto));
    }

    // GET /api/v1/detections/labels —— 目前模型的類別清單，供前端篩選器使用
    [HttpGet("labels")]
    public async Task<IActionResult> Labels(CancellationToken ct)
    {
        var labels = await _modelService.GetLabelsAsync(ct);
        return Ok(labels);
    }

    // 實體轉成 API 回應。
    // 不直接回傳 Entity：避免把資料庫結構（例如內部欄位）洩漏給客戶端，
    // 也避免 EF 的導覽屬性造成循環參照。
    //
    // 時間欄位標記為 UTC 後輸出，客戶端會收到帶 Z 的 ISO 8601 字串。
    private static object ToDto(DetectionRecord r) => new
    {
        id = r.Id,
        capturedAt = DateTime.SpecifyKind(r.CapturedAt, DateTimeKind.Utc),
        receivedAt = DateTime.SpecifyKind(r.ReceivedAt, DateTimeKind.Utc),
        deviceId = r.DeviceId,
        status = r.Status.ToString(),
        modelVersion = r.ModelVersion,
        inferenceMs = r.InferenceMs,
        imageWidth = r.ImageWidth,
        imageHeight = r.ImageHeight,
        detections = r.Objects.Select(o => new
        {
            label = o.Label,
            classId = o.ClassId,
            confidence = o.Confidence,
            x = o.X,
            y = o.Y,
            width = o.Width,
            height = o.Height
        })
    };
}
