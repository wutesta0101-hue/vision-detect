using System.Net.Http.Json;
using System.Text.Json;
using VisionCapture.Models;

namespace VisionCapture.Services;

// 後端 API 的呼叫封裝。
//
// 伺服器位址存在 Preferences（MAUI 的簡易鍵值儲存），
// 使用者可在設定畫面修改 —— 因為每個人的區網 IP 不同，
// 寫死在程式碼裡的話換一台電腦就要重新編譯。
public class ApiService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // 後端回傳 camelCase，這裡的模型是 PascalCase
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private const string BaseUrlKey = "server_base_url";

    // 伺服器位址。
    //
    // 🔴 預設值只是佔位 —— 使用者一定要改成自己電腦的區網 IP。
    //    手機上的 localhost 指的是手機自己，不是你的電腦。
    public string BaseUrl
    {
        get => Preferences.Get(BaseUrlKey, "http://192.168.1.100");
        set => Preferences.Set(BaseUrlKey, value.TrimEnd('/'));
    }

    // 上傳影像，回傳作業識別碼。
    //
    // requestId 由呼叫端提供（拍照當下產生），重送時帶同一個值，
    // 伺服器靠它去重。
    public async Task<SubmitResponse> UploadAsync(
        string filePath, Guid requestId, DateTime capturedAt, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var stream = File.OpenRead(filePath);

        form.Add(new StreamContent(stream), "image", Path.GetFileName(filePath));
        form.Add(new StringContent(requestId.ToString()), "requestId");
        form.Add(new StringContent(DeviceId), "deviceId");
        form.Add(new StringContent(capturedAt.ToString("O")), "capturedAt");

        var response = await _http.PostAsync($"{BaseUrl}/api/v1/detections", form, ct);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<SubmitResponse>(JsonOptions, ct))!;
    }

    // 查詢作業狀態。SignalR 推播不可用時的備援。
    public async Task<DetectionResult?> GetResultAsync(Guid jobId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"{BaseUrl}/api/v1/detections/{jobId}", ct);
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<DetectionResult>(JsonOptions, ct);
    }

    // 連線測試。設定畫面用來確認位址填對了。
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"{BaseUrl}/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // 裝置識別碼。用機型名稱加上一組隨機碼，讓儀表板分得出來源。
    public static string DeviceId
    {
        get
        {
            var id = Preferences.Get("device_id", "");
            if (!string.IsNullOrEmpty(id)) return id;

            id = $"{DeviceInfo.Model}-{Guid.NewGuid().ToString()[..4]}";
            Preferences.Set("device_id", id);
            return id;
        }
    }
}
