using Microsoft.EntityFrameworkCore;
using VisionApi.Core.Abstractions;
using VisionApi.Data;
using VisionApi.Infrastructure.ModelService;
using VisionApi.Infrastructure.Storage;

// 應用進入點。
//
// 第二刀新增：PostgreSQL 資料庫、影像儲存。
// 佇列與 SignalR 會在後續的切片加進來。

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

// 資料庫。連線字串來自 appsettings 或環境變數 ConnectionStrings__Default。
builder.Services.AddDbContext<VisionDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<IDetectionRepository, DetectionRepository>();

// 影像儲存。Singleton 即可 —— 它沒有請求層級的狀態。
builder.Services.AddSingleton<IImageStorage>(_ =>
    new LocalFileImageStorage(builder.Configuration["Storage:ImagePath"] ?? "images"));

var app = builder.Build();

// 開發階段自動建表。
// 正式環境應改用 migration（dotnet ef database update），
// 因為 EnsureCreated 不會處理既有資料表的結構變更。
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<VisionDbContext>().Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

// 健康檢查。部署後 Docker 與監控會打這個端點。
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// 讓整合測試能參照這個組件（WebApplicationFactory 需要）
public partial class Program;
