namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    public event Action<AppSettings>? SettingsApplied;
    public Func<bool>? SendWindowsTestNotification { get; set; }
    public Func<Task<string>>? PreviewNotification { get; set; }
    public Func<Task<string>>? RequestNotificationAccess { get; set; }
    public Func<string>? NotificationStatus { get; set; }
    public void RefreshNotificationStatus() => NotificationStatusText.Text = NotificationStatus?.Invoke() ?? LocalizationManager.TranslateLiteral("通知顯示器尚未就緒");
    private void InitializeNotificationSettings()
    {
        Opened += (_, _) => RefreshNotificationStatus();
        RequestNotificationAccessBtn.Click += async (_, _) =>
        {
            if (RequestNotificationAccess is null) return;
            RequestNotificationAccessBtn.IsEnabled = false;
            try { NotificationStatusText.Text = await RequestNotificationAccess(); }
            catch { NotificationStatusText.Text = LocalizationManager.TranslateLiteral("無法要求通知讀取權限，請稍後重試"); }
            finally { RequestNotificationAccessBtn.IsEnabled = true; }
        };
        TestWindowsNotificationBtn.Click += (_, _) =>
        {
            NotificationStatusText.Text = SendWindowsTestNotification?.Invoke() == true
                ? "測試通知已提交給 Windows；勿擾設定可能抑制通知。請收起 AI / 音樂島查看"
                : "Windows 未接受測試通知；可先按「預覽通知介面」驗證卡片";
        };
        PreviewNotificationBtn.Click += async (_, _) =>
        {
            if (PreviewNotification is null) return;
            PreviewNotificationBtn.IsEnabled = false;
            try { NotificationStatusText.Text = await PreviewNotification(); }
            finally { PreviewNotificationBtn.IsEnabled = true; }
        };
    }
}
