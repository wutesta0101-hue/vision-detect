using System.Net;
using VisionApi.Infrastructure.ModelService;
using Xunit;

namespace VisionApi.Infrastructure.Tests;

// 錯誤分類測試。
//
// 這是韌性策略的判斷依據，也是最容易分錯的地方 —— 
// 把 400 判成可重試，系統就會對著一張損毀的影像重試三次；
// 把 503 判成不可重試，模型服務重啟期間的作業全部白白失敗。
//
// 這些是純函式，測試極快，但守住的是整個重試行為的正確性。
public class ModelServiceErrorsTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]              // 400 影像損毀
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]   // 413 檔案過大
    [InlineData(HttpStatusCode.NotFound)]                // 404 端點不存在
    public void 客戶端錯誤不應重試(HttpStatusCode status)
    {
        Assert.False(ModelServiceErrors.IsRetryable(status));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]          // 408
    [InlineData(HttpStatusCode.TooManyRequests)]         // 429 被限流
    [InlineData(HttpStatusCode.InternalServerError)]     // 500
    [InlineData(HttpStatusCode.BadGateway)]              // 502
    [InlineData(HttpStatusCode.ServiceUnavailable)]      // 503 模型載入中
    [InlineData(HttpStatusCode.GatewayTimeout)]          // 504
    public void 伺服器錯誤與逾時應重試(HttpStatusCode status)
    {
        Assert.True(ModelServiceErrors.IsRetryable(status));
    }

    [Fact]
    public void 未列舉的5xx一律視為可重試()
    {
        // 507 Insufficient Storage 沒有在 switch 明確列出，
        // 應該落到預設分支被判定為可重試。
        Assert.True(ModelServiceErrors.IsRetryable((HttpStatusCode)507));
    }

    [Fact]
    public void 未列舉的4xx不應重試()
    {
        // 418 I'm a teapot —— 任何 4xx 都是客戶端問題
        Assert.False(ModelServiceErrors.IsRetryable((HttpStatusCode)418));
    }

    [Fact]
    public void 成功狀態碼不應被判定為需重試()
    {
        Assert.False(ModelServiceErrors.IsRetryable(HttpStatusCode.OK));
        Assert.False(ModelServiceErrors.IsRetryable(HttpStatusCode.Accepted));
    }
}
