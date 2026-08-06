using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VisionApi.Core.Abstractions;
using VisionApi.Core.Models;

namespace VisionApi.Infrastructure.ModelService;

// 呼叫 Python 推論服務的 HTTP 客戶端。
//
// 這裡是整個系統唯一的跨語言邊界，兩件事在此處理：
//   1. 命名轉換：C# 的 PascalCase ⇄ Python 的 snake_case
//   2. 錯誤分類：把 HTTP 狀態碼翻譯成「可否重試」
//
// 韌性策略（逾時、重試、斷路器）之後用 Polly 加在 HttpClient 上，
// 不寫在這個類別裡 —— 保持它只負責「翻譯」這一件事。
public class ModelServiceClient : IModelServiceClient
{
    private readonly HttpClient _http;

    // snake_case 對應設定。這是本檔案存在的主要理由之一，
    // 有了它，Core 層的 record 可以維持 C# 命名慣例。
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public ModelServiceClient(HttpClient http) => _http = http;

    // POST /infer —— 上傳影像取得偵測框
    public async Task<InferenceResult> InferAsync(
        Stream image,
        string fileName,
        double? confThreshold = null,
        CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent
        {
            { new StreamContent(image), "image", fileName }
        };

        if (confThreshold is not null)
            form.Add(new StringContent(confThreshold.Value.ToString("0.###")), "conf_threshold");

        using var response = await _http.PostAsync("/infer", form, ct);
        await EnsureSuccess(response, ct);

        return (await response.Content.ReadFromJsonAsync<InferenceResult>(JsonOptions, ct))!;
    }

    // GET /labels —— 取得目前模型的類別清單
    public async Task<LabelsResult> GetLabelsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("/labels", ct);
        await EnsureSuccess(response, ct);

        return (await response.Content.ReadFromJsonAsync<LabelsResult>(JsonOptions, ct))!;
    }

    // 把錯誤回應翻譯成帶有「可否重試」資訊的例外。
    //
    // 分類依據（見 CONTRACT.md）：
    //   400 / 413 → 客戶端問題，重試無用
    //   5xx       → 服務端問題，重試可能成功
    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var error = await ReadError(response, ct);
        var retryable = (int)response.StatusCode >= 500;

        throw new ModelServiceException(error.Error, error.Detail, retryable);
    }

    // 解析錯誤內容。服務可能回傳非預期格式（例如反向代理的 HTML 錯誤頁），
    // 因此解析失敗時退回一個通用訊息，而不是讓例外蓋掉原本的錯誤。
    private static async Task<ErrorBody> ReadError(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions, ct);
            if (body is not null && !string.IsNullOrEmpty(body.Error)) return body;
        }
        catch (JsonException) { /* 落到下方的預設值 */ }

        return new ErrorBody("unexpected_response", $"HTTP {(int)response.StatusCode}");
    }

    // 對應契約中的統一錯誤結構
    private record ErrorBody(string Error, string Detail);
}
