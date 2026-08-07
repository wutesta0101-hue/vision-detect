using Microsoft.EntityFrameworkCore;
using VisionApi.BackgroundServices;
using VisionApi.Core.Abstractions;
using VisionApi.Data;
using VisionApi.Hubs;
using VisionApi.Infrastructure.ModelService;
using VisionApi.Infrastructure.Queue;
using VisionApi.Infrastructure.Storage;

// 應用進入點。
//
// 第七刀新增：容器化相關調整 —— 自動建表改由設定控制，
// 因為容器內的環境是 Production，原本的判斷式不會執行。

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// SignalR：辨識完成時主動推播給前端，免輪詢
builder.Services.AddSignalR();

// CORS —— 只在開發階段需要。
//
// 開發時 Vue 跑在 5173、API 跑在別的 port，瀏覽器視為不同來源。
// 部署後兩者都在 Nginx 後面（同一個 origin），這段就不會生效。
//
// 🔴 AllowCredentials 是 SignalR 必要的，而它不能搭配 AllowAnyOrigin，
//    所以一定要明確列出來源。
const string DevCors = "DevCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCors, policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                     ?? new[] { "http://localhost:5173" })
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

// 韌性策略參數，可在 appsettings.json 調整而不需重新編譯
var resilience = builder.Configuration.GetSection("Resilience").Get<ResilienceSettings>()
                 ?? new ResilienceSettings();

// 模型服務客戶端 + 韌性策略。
//
// HttpClient.Timeout 必須大於「Polly 全部嘗試加起來」的時間，
// 否則重試還沒跑完就被外層逾時砍掉。
// 這裡設得寬鬆，實際的時間控制交給 Polly 的逐次逾時。
builder.Services.AddHttpClient<IModelServiceClient, ModelServiceClient>(client =>
{
    var baseUrl = builder.Configuration["ModelService:BaseUrl"] ?? "http://localhost:8000";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromMinutes(2);
})
.AddModelServiceResilience(resilience);

// 資料庫。連線字串來自 appsettings 或環境變數 ConnectionStrings__Default。
builder.Services.AddDbContext<VisionDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<IDetectionRepository, DetectionRepository>();

// 影像儲存。Singleton 即可 —— 它沒有請求層級的狀態。
builder.Services.AddSingleton<IImageStorage>(_ =>
    new LocalFileImageStorage(builder.Configuration["Storage:ImagePath"] ?? "images"));

// 作業佇列必須是 Singleton：所有請求與工作者共用同一個佇列實例。
// 註冊成 Scoped 的話，每個請求會拿到自己的空佇列，工作者永遠收不到東西。
builder.Services.AddSingleton<IJobQueue>(_ =>
    new ChannelJobQueue(builder.Configuration.GetValue("Queue:Capacity", 100)));

// 背景工作者。AddHostedService 會在應用啟動時自動執行它。
builder.Services.AddHostedService<InferenceWorker>();

var app = builder.Build();

// 自動建表。
//
// 🔴 改為由設定控制，不再依 Environment 判斷。
//    容器內的環境是 Production，原本的 IsDevelopment() 永遠是 false，
//    部署後資料表不會建立，API 一啟動就會因為找不到表而失敗。
//
// EnsureCreated 只建立不存在的表，不會修改既有的表 —— 
// 改了實體結構仍需重建資料庫。正式產品應改用 EF Core migration。
if (builder.Configuration.GetValue("Database:AutoCreate", true))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<VisionDbContext>().Database.EnsureCreated();
}

app.UseCors(DevCors);

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapHub<DetectionHub>("/hub/detections");

// 健康檢查。Docker 的 healthcheck 與監控會打這個端點。
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// 讓整合測試能參照這個組件（WebApplicationFactory 需要）
public partial class Program;
