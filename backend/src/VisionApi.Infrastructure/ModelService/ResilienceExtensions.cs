using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace VisionApi.Infrastructure.ModelService;

// 模型服務呼叫的韌性策略。
//
// 三層防護，由內而外包住每一次 HTTP 呼叫：
//
//   ┌─ 斷路器（最外層）：連續失敗就直接拒絕，不再打服務
//   │  ┌─ 重試：可重試的錯誤依退避間隔再試
//   │  │  ┌─ 逾時：單次嘗試的時間上限
//   │  │  │
//   │  │  └─ 實際的 HTTP 呼叫
//
// 順序很重要：逾時要在重試裡面，讓「每次嘗試」各有時間上限，
// 而不是「全部嘗試加起來」共用一個上限。
public static class ResilienceExtensions
{
    // 把韌性策略掛到 HttpClient 上。
    // 呼叫端不需要知道這些策略存在 —— ModelServiceClient 的程式碼完全不用改。
    //
    // 回傳原本的 builder 而非 AddResilienceHandler 的結果，
    // 讓呼叫端可以繼續串接其他 HttpClient 設定。
    public static IHttpClientBuilder AddModelServiceResilience(
        this IHttpClientBuilder builder, ResilienceSettings settings)
    {
        builder.AddResilienceHandler("model-service", pipeline =>
        {
            // ① 逾時（最內層）—— 單次嘗試的上限
            //
            // 沒有這個的話，服務「連得上但不回應」時會一直掛著，
            // 直到 HttpClient 的總逾時才放棄，重試根本沒機會執行。
            pipeline.AddTimeout(TimeSpan.FromSeconds(settings.AttemptTimeoutSeconds));

            // ② 重試 —— 只對可重試的錯誤，且用指數退避
            //
            // 為什麼要退避：服務剛重啟時立刻連打三次只會加重負擔。
            // 間隔拉開讓它有時間恢復。
            pipeline.AddRetry(new Polly.Retry.RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = settings.MaxRetryAttempts,
                Delay = TimeSpan.FromSeconds(settings.BaseDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,   // 加隨機抖動，避免多個客戶端同時重試造成尖峰

                ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome))
            });

            // ③ 斷路器（最外層）—— 服務持續失敗時快速失敗
            //
            // 為什麼需要：服務掛掉時，每個請求都等逾時再重試三次，
            // 會讓佇列越積越多、資源全被卡住。斷路器讓後續請求立刻失敗，
            // 等服務恢復再放行。
            pipeline.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = settings.FailureRatio,
                MinimumThroughput = settings.MinimumThroughput,
                SamplingDuration = TimeSpan.FromSeconds(settings.SamplingDurationSeconds),
                BreakDuration = TimeSpan.FromSeconds(settings.BreakDurationSeconds),

                ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome))
            });
        });

        return builder;
    }

    // 判斷這次結果是否算「失敗」。
    // 重試與斷路器共用同一套判斷，避免兩邊的定義不一致。
    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome)
    {
        // 連不上、DNS 失敗等網路層錯誤
        if (outcome.Exception is HttpRequestException) return true;

        // 單次嘗試逾時
        if (outcome.Exception is TimeoutRejectedException) return true;

        // HTTP 回應依狀態碼分類
        return outcome.Result is not null
            && ModelServiceErrors.IsRetryable(outcome.Result.StatusCode);
    }
}

// 韌性策略的參數。從 appsettings.json 的 Resilience 區段讀取。
//
// 全部可調的理由：這些值沒有標準答案，要依實際的服務行為調整。
// 寫死在程式碼裡的話，每次調整都要重新編譯部署。
public class ResilienceSettings
{
    // 單次嘗試的逾時（秒）。CPU 推論通常 100–500ms，10 秒已經很寬鬆。
    public int AttemptTimeoutSeconds { get; set; } = 10;

    // 重試次數（不含第一次嘗試）
    public int MaxRetryAttempts { get; set; } = 3;

    // 首次重試的間隔（秒）。指數退避後為 2、4、8 秒。
    public int BaseDelaySeconds { get; set; } = 2;

    // 取樣期間內的失敗比率達到此值就斷路
    public double FailureRatio { get; set; } = 0.5;

    // 取樣期間內至少要有這麼多次呼叫，斷路器才會判斷
    // （避免只有一兩次呼叫就誤判）
    public int MinimumThroughput { get; set; } = 4;

    // 取樣期間（秒）
    public int SamplingDurationSeconds { get; set; } = 30;

    // 斷路後保持開啟的時間（秒）。之後會放行一次試探。
    public int BreakDurationSeconds { get; set; } = 15;
}
