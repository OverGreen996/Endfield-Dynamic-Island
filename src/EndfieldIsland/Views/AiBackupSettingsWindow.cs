using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Assistant.Native;

namespace EndfieldChargePlus.Views;

public sealed class AiBackupSettingsWindow:Window
{
    private readonly NativeAssistantService _service;
    private readonly List<Action> _translations=[];
    private readonly TextBox _account=new(){MaxLength=32},_cloudflare=new(){MaxLength=512,PasswordChar='●'},_groq=new(){MaxLength=512,PasswordChar='●'};
    private readonly CheckBox _enabled=new(),_free=new(),_clearCloudflare=new(),_clearGroq=new();
    private readonly TextBlock _feedback=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#1D6473")};
    private readonly Dictionary<string,TextBlock> _statuses=[];
    private readonly List<Button> _actions=[];
    private readonly CancellationTokenSource _lifetime=new();
    private bool _busy;
    public AiBackupSettingsWindow():this(NativeAssistantService.Shared){}
    internal AiBackupSettingsWindow(NativeAssistantService service)
    {
        _service=service;Width=740;Height=760;MinWidth=420;MinHeight=420;
        Background=Brush.Parse("#E0E7E6");Foreground=Brush.Parse("#202728");
        RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light;
        FontFamily=new FontFamily("Segoe UI, Microsoft JhengHei UI");WindowStartupLocation=WindowStartupLocation.CenterScreen;
        foreach(var accent in new[]{"SystemAccentColor","SystemAccentColorLight1","SystemAccentColorLight2","SystemAccentColorLight3","SystemAccentColorDark1","SystemAccentColorDark2","SystemAccentColorDark3"})Resources[accent]=Color.Parse("#149AB2");
        ExtendClientAreaToDecorationsHint=true;ExtendClientAreaChromeHints=Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;ExtendClientAreaTitleBarHeightHint=48;
        var header=new Grid{ColumnDefinitions=new("*,Auto"),Height=48,Background=Brush.Parse("#303A3C")};
        header.Children.Add(new TextBlock{Text="ENDFIELD  /  AI BACKUP",Foreground=Brush.Parse("#8DE1ED"),Margin=new(22,0),VerticalAlignment=VerticalAlignment.Center,FontSize=13});
        var close=new Button{Content="×",Width=48,Height=48,Background=Brushes.Transparent,Foreground=Brushes.White};close.Click+=(_,_)=>Close();Grid.SetColumn(close,1);header.Children.Add(close);
        header.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(header).Properties.IsLeftButtonPressed&&e.Source is not Button)BeginMoveDrag(e);};
        var content=new StackPanel{Spacing=14};
        content.Children.Add(Label("一般助理，自動接手。","Your assistant, with automatic fallback.",25));
        content.Children.Add(Label("Gemini → Groq GPT-OSS 120B → Cloudflare Qwen3.8-27B\n文字理解、搜尋整理與記憶判斷共用此順序。圖片文字使用 Windows 本機 OCR。","Gemini → Groq GPT-OSS 120B → Cloudflare Qwen3.8-27B\nOne order for text understanding, search synthesis and memory decisions. Windows OCR reads image text locally."));
        Bind(_enabled,"啟用 AI 自動備援","Enable automatic AI fallback");_enabled.IsChecked=_service.Backups.Preferences.Enabled;content.Children.Add(_enabled);
        content.Children.Add(Card("Groq  /  GPT-OSS 120B",groq:true));
        content.Children.Add(Card("Cloudflare  /  Qwen3.8-27B",groq:false));
        Bind(_free,"我使用兩家的免費方案，未啟用付費。","I use the providers' free plans with paid billing disabled.");_free.IsChecked=_service.Backups.Preferences.FreeConfirmed;content.Children.Add(_free);
        content.Children.Add(Label("金鑰只以 Windows 使用者加密保存。空白保留已存資料；更換金鑰保留用量紀錄。儲存不發出 API 請求，按測試才會使用一次模型。","Keys are encrypted for your Windows user. Blank inputs keep saved values; replacing keys preserves usage records. Saving makes no API request; each test uses one generation."));
        var save=Action("儲存並立即套用","Save and apply",SaveAsync);save.Background=Brush.Parse("#D8DF43");save.HorizontalAlignment=HorizontalAlignment.Right;content.Children.Add(save);content.Children.Add(_feedback);
        var scroll=new ScrollViewer{Content=content,Margin=new(24,20),HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
        var root=new Grid{RowDefinitions=new("48,*"),Background=Background};root.Children.Add(header);Grid.SetRow(scroll,1);root.Children.Add(scroll);Content=root;
        _translations.Add(()=>Title=T("AI 模型備援","AI model fallback"));
        _translations.Add(()=>Avalonia.Automation.AutomationProperties.SetName(close,T("關閉 AI 備援設定","Close AI fallback settings")));
        LocalizationManager.LanguageChanged+=Translate;
        Closed+=(_,_)=>{_lifetime.Cancel();_account.Text=_cloudflare.Text=_groq.Text="";LocalizationManager.LanguageChanged-=Translate;};
        KeyDown+=(_,e)=>{if(e.Key==Avalonia.Input.Key.Escape)Close();};Translate();
    }
    private static string T(string zh,string en)=>LocalizationManager.Text(zh,en);
    private TextBlock Label(string zh,string en,double size=13)
    {
        var label=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=size};_translations.Add(()=>label.Text=T(zh,en));return label;
    }
    private void Bind(CheckBox box,string zh,string en)=>_translations.Add(()=>box.Content=T(zh,en));
    private Button Action(string zh,string en,Func<Task> action)
    {
        var button=new Button{Padding=new(14,9)};_translations.Add(()=>button.Content=T(zh,en));
        button.Click+=async(_,_)=>{if(_busy)return;_busy=true;SetInputsEnabled(false);try{await action();}catch(OperationCanceledException){}catch(Exception ex){if(!_lifetime.IsCancellationRequested)_feedback.Text=ex is AssistantFailure failure?AssistantClient.ErrorText(failure.Code):T("操作未完成，請檢查設定與網路。","Operation did not finish. Check settings and network.");}finally{_busy=false;if(!_lifetime.IsCancellationRequested){SetInputsEnabled(true);RefreshStatus();}}};
        _actions.Add(button);return button;
    }
    private Border Card(string title,bool groq)
    {
        var panel=new StackPanel{Spacing=10};panel.Children.Add(new TextBlock{Text=title,FontSize=17,Foreground=Brush.Parse("#1D6473")});
        if(!groq){panel.Children.Add(Label("Cloudflare Account ID（32 碼）","Cloudflare Account ID (32 characters)"));panel.Children.Add(_account);}
        panel.Children.Add(Label(groq?"Groq API Key":"Cloudflare API Token",groq?"Groq API Key":"Cloudflare API Token"));
        var input=groq?_groq:_cloudflare;panel.Children.Add(input);
        _translations.Add(()=>{input.Watermark=T("貼上金鑰；空白保留已儲存資料","Paste key; leave blank to keep the saved value");_account.Watermark=T("貼上 Account ID；空白保留已儲存資料","Paste Account ID; leave blank to keep the saved value");});
        Avalonia.Automation.AutomationProperties.SetName(input,groq?"Groq API Key":"Cloudflare API Token");Avalonia.Automation.AutomationProperties.SetName(_account,"Cloudflare Account ID");
        var clear=groq?_clearGroq:_clearCloudflare;Bind(clear,"移除此服務的已存金鑰","Remove this provider's saved credentials");panel.Children.Add(clear);
        var id=groq?"groq":"cloudflare";var status=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12};_statuses[id]=status;panel.Children.Add(status);
        panel.Children.Add(Action("測試此服務（一次請求）","Test provider (one request)",async()=>{await Task.Run(()=>_service.ProbeBackupAsync(id,_lifetime.Token),_lifetime.Token);if(!_lifetime.IsCancellationRequested)_feedback.Text=T("連線與 JSON 回覆測試通過。","Connection and JSON response test passed.");}));
        return new Border{Child=panel,Padding=new(16),CornerRadius=new(8),Background=Brush.Parse("#F2F5F3"),BorderBrush=Brush.Parse("#A1B4B8"),BorderThickness=new(1)};
    }
    private async Task SaveAsync()
    {
        var account=_account.Text??"";var cf=_cloudflare.Text??"";var groq=_groq.Text??"";
        var enabled=_enabled.IsChecked==true;var free=_free.IsChecked==true;var clearCf=_clearCloudflare.IsChecked==true;var clearGroq=_clearGroq.IsChecked==true;
        await Task.Run(()=>_service.ConfigureBackupsAsync(account,cf,groq,enabled,free,clearCf,clearGroq,_lifetime.Token),_lifetime.Token);
        if(_lifetime.IsCancellationRequested)return;
        _account.Text=_cloudflare.Text=_groq.Text="";_clearCloudflare.IsChecked=_clearGroq.IsChecked=false;
        _feedback.Text=T("已加密保存並立即套用；沒有呼叫模型。","Encrypted and applied immediately. No model call was made.");
    }
    private void Translate(){foreach(var update in _translations)update();RefreshStatus();}
    private void SetInputsEnabled(bool enabled)
    {
        foreach(var button in _actions)button.IsEnabled=enabled;
        foreach(var input in new Control[]{_account,_cloudflare,_groq,_enabled,_free,_clearCloudflare,_clearGroq})input.IsEnabled=enabled;
    }
    private void RefreshStatus()
    {
        foreach(var info in _service.Backups.Status()){
            var reason=info.Reason switch{"ready"=>T("可使用","Ready"),"not_configured"=>T("尚未設定","Not configured"),"disabled"=>T("已停用","Disabled"),"auth"=>T("金鑰或權限錯誤，請更換資料","Invalid credentials or permission; replace credentials"),"free_unconfirmed"=>T("尚未確認免費方案","Free plan not confirmed"),"backup_storage_error"=>T("資料無法讀取；原檔保留","Unable to read local data; existing files preserved"),_=>T("暫停，使用下一家備援","Paused; next provider takes over")};
            var retry=info.RetryAt>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()?T(" · 重試 "," · Retry ")+DateTimeOffset.FromUnixTimeMilliseconds(info.RetryAt).ToOffset(TimeSpan.FromHours(8)).ToString("MM/dd HH:mm"):"";
            _statuses[info.Id].Text=reason+retry+T($" · 本機累計 {info.Requests} 次／{info.Tokens} tokens",$" · Local total {info.Requests} requests / {info.Tokens} tokens");
        }
    }
}
