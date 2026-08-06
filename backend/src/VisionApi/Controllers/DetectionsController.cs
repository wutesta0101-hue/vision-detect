using Microsoft.AspNetCore.Mvc;
using VisionApi.Core.Abstractions;

namespace VisionApi.Controllers;

// 辨識端點。
//
// ⚠️ 目前是同步版本：收到上傳後直接呼叫模型服務並等待結果回傳。
//    這是第一刀，先確認跨語言邊界打通。
//    之後會改成非同步（回 202 + jobId，背景工作者處理），屆時本檔案會大改。
[ApiController]
[Route("api/v1/[controller]")]
public class DetectionsController : ControllerBase
{
    private readonly IModelServiceClient _modelService;

    public DetectionsController(IModelServiceClient modelService) => _modelService = modelService;

    // POST /api/v1/detections —— 上傳影像取得辨識結果
    [HttpPost]
    public async Task<IActionResult> Detect(
        IFormFile image,
        [FromForm] double? confThreshold,
        CancellationToken ct)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { error = "missing_image", detail = "請提供影像檔案" });

        await using var stream = image.OpenReadStream();

        try
        {
            var result = await _modelService.InferAsync(stream, image.FileName, confThreshold, ct);
            return Ok(result);
        }
        catch (ModelServiceException ex)
        {
            // 不可重試的錯誤代表輸入有問題 → 回 400 讓客戶端知道要改輸入
            // 可重試的錯誤代表服務暫時不可用 → 回 503
            var status = ex.Retryable ? 503 : 400;
            return StatusCode(status, new { error = ex.ErrorCode, detail = ex.Message });
        }
    }

    // GET /api/v1/detections/labels —— 目前模型的類別清單，供前端篩選器使用
    [HttpGet("labels")]
    public async Task<IActionResult> Labels(CancellationToken ct)
    {
        var labels = await _modelService.GetLabelsAsync(ct);
        return Ok(labels);
    }
}
