using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EndfieldChargePlus.Assistant.Native;

namespace EndfieldChargePlus.Views;

public sealed class GeminiPoolWindow:Window
{
    private readonly NativeAssistantService _service;
    private readonly List<Action> _translations=[];
    private readonly TextBox[] _keys=Enumerable.Range(2,4).Select(i=>new TextBox{Name="GeminiPoolKey"+i,PasswordChar='●',MaxLength=160}).ToArray();
    private readonly CheckBox[] _clear=Enumerable.Range(2,4).Select(_=>new CheckBox()).ToArray();
    private readonly TextBlock[] _status=Enumerable.Range(1,5).Select(_=>new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Brush.Parse("#345D64")}).ToArray();
    private readonly CheckBox _free=new();
    private readonly TextBlock _feedback=new(){TextWrapping=TextWrapping.Wrap,FontSize=12};
    private readonly Button _save;
    private readonly CancellationTokenSource _lifetime=new();
    private bool _busy;
    public GeminiPoolWindow():this(NativeAssistantService.Shared){}
    internal GeminiPoolWindow(NativeAssistantService service)
    {
        _service=service;Width=780;Height=810;MinWidth=460;MinHeight=460;Background=Brush.Parse("#E3E9E7");Foreground=Brush.Parse("#202C2F");RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light;
        FontFamily=new("Segoe UI, Microsoft JhengHei UI");WindowStartupLocation=WindowStartupLocation.CenterScreen;
        foreach(var key in new[]{"SystemAccentColor","SystemAccentColorLight1","SystemAccentColorLight2","SystemAccentColorLight3","SystemAccentColorDark1","SystemAccentColorDark2","SystemAccentColorDark3"})Resources[key]=Color.Parse("#149AB2");
        ExtendClientAreaToDecorationsHint=true;ExtendClientAreaChromeHints=Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;ExtendClientAreaTitleBarHeightHint=48;
        var header=new Grid{Height=48,ColumnDefinitions=new("*,Auto"),Background=Brush.Parse("#252F32")};
        header.Children.Add(new TextBlock{Text="ENDFIELD  /  GEMINI PRIORITY",Margin=new(22,0),FontSize=12,Foreground=Brush.Parse("#91DDE9"),VerticalAlignment=VerticalAlignment.Center});
        var close=new Button{Content="×",Width=48,Height=48,Background=Brushes.Transparent,Foreground=Brushes.White};close.Click+=(_,_)=>Close();Grid.SetColumn(close,1);header.Children.Add(close);
        _translations.Add(()=>Avalonia.Automation.AutomationProperties.SetName(close,T("關閉 Gemini 輪換設定","Close Gemini rotation settings")));
        header.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(header).Properties.IsLeftButtonPressed&&e.Source is not Button)BeginMoveDrag(e);};
        var content=new StackPanel{Spacing=16};content.Children.Add(Label("五組主力，依序接手。","Five primaries. One priority order.",26));
        content.Children.Add(Label("第 1 組持續使用，受限才換第 2 組，依序到第 5 組；全部不可用才接 Groq → Cloudflare。每日額度恢復後優先回第 1 組。對話、人格與記憶沿用，不需要重新說明。","Keep using account 1 until unavailable, then account 2, through account 5. Only then use Groq → Cloudflare. Return to account 1 when its quota resets. Context, personality and memory continue unchanged.",13));
        content.Children.Add(Label("第 1 組沿用 AI 助理頁面的主金鑰與原本用量。下面可加入另外 4 個帳號。Google 額度按專案計算，同專案不同金鑰仍共享額度；多帳號使用仍受官方配額與條款約束。","Slot 1 keeps the primary key and its existing usage from AI Assistant settings. Add four other accounts below. Google quotas are per project; keys from the same project share quota. Multi-account use remains subject to Google quotas and terms.",12));
        var primary=new StackPanel{Spacing=8};primary.Children.Add(new TextBlock{Text="01  /  GEMINI",FontSize=16,Foreground=Brush.Parse("#17677A")});primary.Children.Add(_status[0]);primary.Children.Add(Label("主金鑰請回「設定 → AI 助理」修改。","Edit the primary key under Settings → AI Assistant.",12));content.Children.Add(Card(primary));
        for(var i=0;i<4;i++){
            var panel=new StackPanel{Spacing=8};panel.Children.Add(new TextBlock{Text=$"{i+2:00}  /  GEMINI",FontSize=16,Foreground=Brush.Parse("#17677A")});
            var label=Label("此帳號的 Gemini API Key","Gemini API key for this account",12);panel.Children.Add(label);panel.Children.Add(_keys[i]);panel.Children.Add(_status[i+1]);panel.Children.Add(_clear[i]);
            var index=i;_translations.Add(()=>{_keys[index].Watermark=T("貼上金鑰；空白保留原本資料","Paste key; blank keeps saved credentials");_clear[index].Content=T("移除此組金鑰","Remove this account's saved key");Avalonia.Automation.AutomationProperties.SetName(_keys[index],$"Gemini {index+2} API Key");});content.Children.Add(Card(panel));
        }
        _translations.Add(()=>_free.Content=T("這些帳號的專案皆使用免費方案，未啟用付費。","These accounts' projects use free plans with paid billing disabled."));content.Children.Add(_free);
        content.Children.Add(Label("沒有本機用量上限。各組依 API 回應暫停，日額度按太平洋時間午夜恢復（台灣夏令 15:00／冬令 16:00）；短暫限流按 API 重試時間。圖片辨識也輪換這五組，文字備援不會收到圖片。","No local usage caps. Each account pauses according to API responses. Daily quota resets at Pacific midnight (15:00 Taipei in summer, 16:00 in winter); temporary rate limits follow API retry times. Images also rotate across these Gemini accounts; text-only backups never receive images.",12));
        var scroll=new ScrollViewer{Content=content,Margin=new(26,22,26,10),HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
        var footer=new StackPanel{Spacing=10,Margin=new(26,10,26,22)};var actions=new WrapPanel();
        _save=new Button{Name="GeminiPoolSave",MinHeight=42,Padding=new(16,9),Background=Brush.Parse("#147F94"),Foreground=Brushes.White,Margin=new(0,0,10,6)};
        _translations.Add(()=>_save.Content=T("儲存並套用輪換","Save and apply priority"));_save.Click+=async(_,_)=>await SaveAsync();actions.Children.Add(_save);
        var refresh=new Button{Name="GeminiPoolRefresh",MinHeight=42,Padding=new(16,9),Margin=new(0,0,0,6)};_translations.Add(()=>refresh.Content=T("刷新本機狀態","Refresh local status"));refresh.Click+=(_,_)=>RefreshStatus();actions.Children.Add(refresh);footer.Children.Add(actions);footer.Children.Add(_feedback);
        var root=new Grid{RowDefinitions=new("48,*,Auto"),Background=Background};root.Children.Add(header);Grid.SetRow(scroll,1);root.Children.Add(scroll);Grid.SetRow(footer,2);root.Children.Add(footer);Content=root;
        LocalizationManager.LanguageChanged+=Translate;_translations.Add(()=>Title=T("Gemini 五組主力輪換","Five-account Gemini priority"));
        Closed+=(_,_)=>{_lifetime.Cancel();foreach(var key in _keys)key.Text="";LocalizationManager.LanguageChanged-=Translate;};
        KeyDown+=(_,e)=>{if(e.Key==Avalonia.Input.Key.Escape)Close();};Translate();
    }
    private static string T(string zh,string en)=>LocalizationManager.Text(zh,en);
    private TextBlock Label(string zh,string en,double size){var label=new TextBlock{FontSize=size,TextWrapping=TextWrapping.Wrap};_translations.Add(()=>label.Text=T(zh,en));return label;}
    private static Border Card(Control child)=>new(){Child=child,Padding=new(16),Background=Brush.Parse("#F5F7F4"),BorderBrush=Brush.Parse("#A8BBBE"),BorderThickness=new(1),CornerRadius=new(6)};
    private void Translate(){foreach(var update in _translations)update();RefreshStatus();}
    private void RefreshStatus()
    {
        foreach(var slot in _service.GeminiPool.Status()){
            var reason=slot.Reason switch{"ready"=>T("可使用","Ready"),"not_configured"=>T("尚未設定","Not configured"),"provider_daily_quota"=>T("日額度已滿","Daily quota exhausted"),"provider_auth"=>T("金鑰或權限錯誤","Invalid key or permissions"),"storage_error"=>T("資料無法讀取，原檔保留","Cannot read local data; original preserved"),_=>T("暫停，先用下一組","Paused; next account takes over")};
            var retry=slot.RetryAt>0?T(" · 重試 "," · Retry ")+DateTimeOffset.FromUnixTimeMilliseconds(slot.RetryAt).ToOffset(TimeSpan.FromHours(8)).ToString("MM/dd HH:mm"):"";
            _status[slot.Slot-1].Text=reason+retry+T($" · 今日 {slot.Requests} 次 / {slot.Tokens:N0} tokens",$" · Today {slot.Requests} requests / {slot.Tokens:N0} tokens");
        }
        _save.IsEnabled=!_busy&&!_service.GeminiPool.StorageError;
    }
    private async Task SaveAsync()
    {
        if(_busy)return;_busy=true;_save.IsEnabled=false;
        var inputs=_keys.Select(k=>k.Text??"").ToArray();var clear=Enumerable.Range(0,4).Where(i=>_clear[i].IsChecked==true).Select(i=>i+2).ToHashSet();var confirmed=_free.IsChecked==true;
        foreach(var control in _keys.Cast<Control>().Concat(_clear).Append(_free))control.IsEnabled=false;
        try{
            await Task.Run(()=>_service.ConfigureGeminiPoolAsync(inputs,clear,confirmed,_lifetime.Token),_lifetime.Token);
            if(!_lifetime.IsCancellationRequested){foreach(var key in _keys)key.Text="";foreach(var check in _clear)check.IsChecked=false;_feedback.Text=T("金鑰已加密保存；從下一輪套用。儲存沒有呼叫模型。","Keys encrypted and saved. Applies next turn. Saving made no model request.");}
        }catch(OperationCanceledException){}
        catch(Exception ex){if(!_lifetime.IsCancellationRequested)_feedback.Text=ex is AssistantFailure error&&error.Code=="duplicate_gemini_key"?T("金鑰重複。請填入不同帳號的金鑰；原資料保留。","Duplicate key. Use keys from different accounts; original data kept."):!confirmed?T("請先確認這些帳號使用免費方案。","Confirm these accounts use free plans first."):T("套用未完成。請檢查金鑰格式；原本資料與輸入保留。","Could not apply. Check key format; saved data and inputs kept.");}
        finally{_busy=false;if(!_lifetime.IsCancellationRequested){foreach(var control in _keys.Cast<Control>().Concat(_clear).Append(_free))control.IsEnabled=true;RefreshStatus();}}
    }
}
