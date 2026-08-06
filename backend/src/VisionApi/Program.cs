using VisionApi.Core.Abstractions;
using VisionApi.Infrastructure.ModelService;

// 應用進入點。
//
// 目前只註冊三樣東西：控制器、Swagger、模型服務客戶端。
// 資料庫、佇列、SignalR 會在後續的切片加進來。

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 模型服務客戶端。
// 用 AddHttpClient 而不是 new HttpClient()：前者會管理連線池，
// 也是之後掛上 Polly 韌性策略的接點。
builder.Services.AddHttpClient<IModelServiceClient, ModelServiceClient>(client =>
{
    var baseUrl = builder.Configuration["ModelService:BaseUrl"] ?? "http://localhost:8000";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

// 健康檢查。部署後 Docker 與監控會打這個端點。
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// 讓整合測試能參照這個組件（WebApplicationFactory 需要）
public partial class Program;
