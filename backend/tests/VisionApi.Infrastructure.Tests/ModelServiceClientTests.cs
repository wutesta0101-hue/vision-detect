using System.Net;
using System.Text;
using VisionApi.Core.Abstractions;
using VisionApi.Infrastructure.ModelService;
using Xunit;

namespace VisionApi.Infrastructure.Tests;

// 模型服務客戶端的契約測試。
//
// 不啟動真正的 Python 服務，改用假的 HTTP 處理器回傳固定內容。
// 這樣測試跑得快，而且能精準模擬各種失敗情境 —— 那才是真正要驗證的部分。
public class ModelServiceClientTests
{
    // 假的 HTTP 處理器：不管收到什麼請求，都回傳預設好的回應
    private class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
    }

    // 建立掛著假處理器的客戶端
    private static ModelServiceClient MakeClient(HttpStatusCode status, string body) =>
        new(new HttpClient(new StubHandler(status, body))
        {
            BaseAddress = new Uri("http://model:8000")
        });

    private static Stream FakeImage() => new MemoryStream(new byte[] { 1, 2, 3 });

    private const string SuccessBody = """
    {
      "model_version": "yolov8n-coco",
      "inference_ms": 120,
      "image_width": 1920,
      "image_height": 1080,
      "detections": [
        {"label":"person","class_id":0,"confidence":0.91,"x":10,"y":20,"width":30,"height":40}
      ]
    }
    """;

    // ---------- 正常路徑：snake_case 要正確對應到 C# 屬性 ----------

    [Fact]
    public async Task 成功回應應正確反序列化()
    {
        var client = MakeClient(HttpStatusCode.OK, SuccessBody);

        var result = await client.InferAsync(FakeImage(), "t.jpg");

        Assert.Equal("yolov8n-coco", result.ModelVersion);
        Assert.Equal(120, result.InferenceMs);
        Assert.Equal(1920, result.ImageWidth);
        Assert.Equal(1080, result.ImageHeight);
    }

    [Fact]
    public async Task 偵測框欄位應正確對應()
    {
        var client = MakeClient(HttpStatusCode.OK, SuccessBody);

        var box = (await client.InferAsync(FakeImage(), "t.jpg")).Detections.Single();

        Assert.Equal("person", box.Label);
        Assert.Equal(0, box.ClassId);       // class_id → ClassId
        Assert.Equal(0.91, box.Confidence, 2);
        Assert.Equal(10, box.X);
        Assert.Equal(30, box.Width);
    }

    [Fact]
    public async Task 空結果不應拋出例外()
    {
        const string body = """
        {"model_version":"v1","inference_ms":50,"image_width":640,"image_height":480,"detections":[]}
        """;
        var client = MakeClient(HttpStatusCode.OK, body);

        var result = await client.InferAsync(FakeImage(), "t.jpg");

        Assert.Empty(result.Detections);
    }

    // ---------- 錯誤分類：這組決定上層的重試策略 ----------

    [Fact]
    public async Task 影像損毀應標記為不可重試()
    {
        var client = MakeClient(HttpStatusCode.BadRequest,
            """{"error":"invalid_image","detail":"無法解析影像"}""");

        var ex = await Assert.ThrowsAsync<ModelServiceException>(
            () => client.InferAsync(FakeImage(), "t.jpg"));

        Assert.Equal("invalid_image", ex.ErrorCode);
        Assert.False(ex.Retryable);   // 重試一百次也不會成功
    }

    [Fact]
    public async Task 檔案過大應標記為不可重試()
    {
        var client = MakeClient(HttpStatusCode.RequestEntityTooLarge,
            """{"error":"image_too_large","detail":"超過上限"}""");

        var ex = await Assert.ThrowsAsync<ModelServiceException>(
            () => client.InferAsync(FakeImage(), "t.jpg"));

        Assert.False(ex.Retryable);
    }

    [Fact]
    public async Task 模型未就緒應標記為可重試()
    {
        var client = MakeClient(HttpStatusCode.ServiceUnavailable,
            """{"error":"model_not_ready","detail":"載入中"}""");

        var ex = await Assert.ThrowsAsync<ModelServiceException>(
            () => client.InferAsync(FakeImage(), "t.jpg"));

        Assert.Equal("model_not_ready", ex.ErrorCode);
        Assert.True(ex.Retryable);    // 等一下再試就會好
    }

    [Fact]
    public async Task 伺服器錯誤應標記為可重試()
    {
        var client = MakeClient(HttpStatusCode.InternalServerError,
            """{"error":"inference_error","detail":"未預期錯誤"}""");

        var ex = await Assert.ThrowsAsync<ModelServiceException>(
            () => client.InferAsync(FakeImage(), "t.jpg"));

        Assert.True(ex.Retryable);
    }

    [Fact]
    public async Task 非預期格式的錯誤回應不應讓解析例外蓋掉原始錯誤()
    {
        // 例如反向代理回傳 HTML 錯誤頁的情況
        var client = MakeClient(HttpStatusCode.BadGateway, "<html>502</html>");

        var ex = await Assert.ThrowsAsync<ModelServiceException>(
            () => client.InferAsync(FakeImage(), "t.jpg"));

        Assert.Equal("unexpected_response", ex.ErrorCode);
        Assert.True(ex.Retryable);
    }

    // ---------- /labels ----------

    [Fact]
    public async Task 類別清單應正確反序列化()
    {
        const string body = """
        {"model_version":"v1","count":2,
         "labels":[{"class_id":0,"label":"person"},{"class_id":1,"label":"bicycle"}]}
        """;
        var client = MakeClient(HttpStatusCode.OK, body);

        var result = await client.GetLabelsAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("person", result.Labels[0].Label);
    }
}
