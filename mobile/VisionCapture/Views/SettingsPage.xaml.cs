using VisionCapture.Services;

namespace VisionCapture.Views;

// 設定畫面：伺服器位址的填寫與連線測試。
//
// 「測試連線」按鈕的價值：位址填錯時，使用者會在這裡就發現，
// 而不是拍完照上傳失敗才知道。
public partial class SettingsPage : ContentPage
{
    private readonly ApiService _api;

    public SettingsPage(ApiService api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UrlEntry.Text = _api.BaseUrl;
        DeviceIdLabel.Text = ApiService.DeviceId;
    }

    // 測試連線。打的是後端的 /health 端點。
    private async void OnTestClicked(object sender, EventArgs e)
    {
        var original = _api.BaseUrl;
        _api.BaseUrl = UrlEntry.Text?.Trim() ?? "";

        TestResult.IsVisible = true;
        TestResult.Text = "測試中…";
        TestResult.TextColor = Color.FromArgb("#8E8E93");

        var ok = await _api.PingAsync();

        TestResult.Text = ok ? "✓ 連線成功" : "✗ 連不上，請檢查位址與 Wi-Fi";
        TestResult.TextColor = Color.FromArgb(ok ? "#34C759" : "#FF3B30");

        // 測試失敗就還原原本的設定，避免覆蓋掉本來能用的位址
        if (!ok) _api.BaseUrl = original;
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        _api.BaseUrl = UrlEntry.Text?.Trim() ?? "";
        await DisplayAlert("已儲存", $"伺服器位址：{_api.BaseUrl}", "確定");
        await Shell.Current.GoToAsync("//camera");
    }
}
