using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EndfieldChargePlus.Assistant.Native;

namespace EndfieldChargePlus.Views;

/// <summary>Local native settings; secrets never enter a browser or a loopback service.</summary>
internal sealed class SearchSettingsWindow : Window
{
    private static readonly IBrush BackgroundBrush=Brush.Parse("#202728"), Surface=Brush.Parse("#2A3335"), Ink=Brush.Parse("#F1F3F0"), Muted=Brush.Parse("#A5B4B7"), Cyan=Brush.Parse("#13C8E8");
    private readonly Dictionary<string,(CheckBox Enabled,TextBox Key,CheckBox Clear,TextBlock Status)> _rows=[];
    private readonly NativeSearch _search;
    private readonly StackPanel _content=new(){Spacing=14};
    private readonly TextBlock _feedback=new(){TextWrapping=TextWrapping.Wrap,Foreground=Cyan};
    private readonly CheckBox _enabled=new();
    private readonly CheckBox _rotate=new();
    private readonly ComboBox _order=new(){HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly CancellationTokenSource _lifetime=new();
    private readonly List<Button> _buttons=[];
    private JsonObject? _settings;
    private readonly NumericUpDown _billingDay=new(){Minimum=1,Maximum=31,Increment=1,FormatString="0",Width=110};
    private readonly TimePicker _billingTime=new(){ClockIdentifier="24HourClock",Width=180};
    private bool _busy;
    private static readonly string[][] Orders=[["exa","tavily","firecrawl"],["exa","firecrawl","tavily"],["tavily","exa","firecrawl"],["tavily","firecrawl","exa"],["firecrawl","exa","tavily"],["firecrawl","tavily","exa"]];
    private static string T(string zh,string en)=>LocalizationManager.Text(zh,en);
    internal SearchSettingsWindow(NativeSearch? search=null)
    {
        _search=search??NativeAssistantService.Shared.Search;
        Title=T("搜尋 API 與輪替","Search API & rotation");Width=820;Height=720;MinWidth=600;MinHeight=500;
        Background=BackgroundBrush;Foreground=Ink;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;
        foreach(var accent in new[]{"SystemAccentColor","SystemAccentColorLight1","SystemAccentColorLight2","SystemAccentColorLight3","SystemAccentColorDark1","SystemAccentColorDark2","SystemAccentColorDark3"})Resources[accent]=Color.Parse("#13C8E8");
        ExtendClientAreaToDecorationsHint=true;ExtendClientAreaChromeHints=Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;ExtendClientAreaTitleBarHeightHint=48;
        var header=new Grid{ColumnDefinitions=new("*,Auto"),Height=48,Background=Surface};
        var title=new TextBlock{Text="ENDFIELD  /  "+T("搜尋控制","SEARCH CONTROL"),FontSize=13,FontWeight=FontWeight.SemiBold,Foreground=Cyan,Margin=new(22,0),VerticalAlignment=VerticalAlignment.Center};header.Children.Add(title);
        var close=new Button{Content="×",Width=48,Height=48,Background=Brushes.Transparent,Foreground=Ink};close.Click+=(_,_)=>Close();Grid.SetColumn(close,1);header.Children.Add(close);
        Avalonia.Automation.AutomationProperties.SetName(close,T("關閉搜尋設定","Close search settings"));
        header.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(header).Properties.IsLeftButtonPressed&&e.Source is not Button)BeginMoveDrag(e);};
        var heading=new TextBlock{Text=T("三家搜尋，一個流程。","Three providers. One search flow."),FontSize=26,FontWeight=FontWeight.SemiBold};_content.Children.Add(heading);
        _content.Children.Add(new TextBlock{Text=T("只供靈動島使用。金鑰加密保存在此電腦；Daily 的設定與額度獨立。","Island only. Keys are encrypted on this computer; Daily settings and usage are independent."),TextWrapping=TextWrapping.Wrap,Foreground=Muted,FontSize=13});
        _enabled.Content=T("啟用搜尋與自動備援","Enable search and automatic fallback");_content.Children.Add(_enabled);
        _content.Children.Add(new TextBlock{Text=T("優先順序","PROVIDER ORDER"),Foreground=Muted,FontSize=11});
        _order.ItemsSource=Orders.Select(o=>string.Join("   →   ",o.Select(ProviderName))).ToArray();_content.Children.Add(_order);
        _rotate.Content=T("每輪搜尋換一家","Rotate provider after each search turn");_content.Children.Add(_rotate);
        _content.Children.Add(new TextBlock{Text=T("依上方順序循環，跳過不可用的 API。純聊天、快取命中與取消不輪替；關閉時使用優先順序。","Cycle through the order above and skip unavailable APIs. Chat, cached results and cancellations do not advance rotation. Turn off to use priority order."),TextWrapping=TextWrapping.Wrap,Foreground=Muted,FontSize=12});
        foreach(var id in NativeSearch.Ids)AddProvider(id);
        var save=ActionButton(T("儲存設定","Save settings"),SaveAsync);save.Background=Brush.Parse("#D8DF43");save.Foreground=Brush.Parse("#202728");save.HorizontalAlignment=HorizontalAlignment.Right;save.MinWidth=150;_content.Children.Add(save);
        _content.Children.Add(_feedback);
        var scroll=new ScrollViewer{Content=_content,Margin=new(24,20),HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
        var shell=new Grid{RowDefinitions=new("48,*"),Background=BackgroundBrush};shell.Children.Add(header);Grid.SetRow(scroll,1);shell.Children.Add(scroll);Content=shell;
        Opened+=async(_,_)=>{try{await LoadAsync();}catch(OperationCanceledException){}catch{_feedback.Text=T("本機設定無法讀取，原資料保留。","Unable to read local settings. Existing data is preserved.");}};Closed+=(_,_)=>{_lifetime.Cancel();};
        KeyDown+=(_,e)=>{if(e.Key==Avalonia.Input.Key.Escape)Close();};
    }
    private static string ProviderName(string id)=>id=="exa"?"Exa Auto":id=="tavily"?"Tavily Basic":"Firecrawl Search";
    private void AddProvider(string id)
    {
        var panel=new StackPanel{Spacing=10};var enabled=new CheckBox{Content=ProviderName(id),FontSize=17,FontWeight=FontWeight.SemiBold};panel.Children.Add(enabled);
        var state=new TextBlock{Foreground=Muted,FontSize=12,TextWrapping=TextWrapping.Wrap};panel.Children.Add(state);
        panel.Children.Add(new TextBlock{Text="API Key",Foreground=Muted,FontSize=12});
        var key=new TextBox{PasswordChar='●',Watermark=T("貼上新金鑰；留空保留目前金鑰","Paste a new key; leave blank to keep the existing key"),HorizontalAlignment=HorizontalAlignment.Stretch};
        Avalonia.Automation.AutomationProperties.SetName(key,ProviderName(id)+" API Key");panel.Children.Add(key);
        var clear=new CheckBox{Content=T("移除此金鑰","Remove this key"),Margin=new(0,0,12,0)};panel.Children.Add(clear);
        if(id=="firecrawl"){
            var billing=new WrapPanel{Orientation=Orientation.Horizontal};
            var day=new StackPanel{Spacing=6,Margin=new(0,0,20,6)};day.Children.Add(new TextBlock{Text=T("每月帳單切換日","Monthly renewal day"),Foreground=Muted,FontSize=12});day.Children.Add(_billingDay);billing.Children.Add(day);
            var time=new StackPanel{Spacing=6,Margin=new(0,0,20,6)};time.Children.Add(new TextBlock{Text=T("切換時間（台灣）","Renewal time (Taiwan)"),Foreground=Muted,FontSize=12});time.Children.Add(_billingTime);billing.Children.Add(time);panel.Children.Add(billing);
            Avalonia.Automation.AutomationProperties.SetName(_billingDay,T("Firecrawl 每月帳單切換日","Firecrawl monthly renewal day"));
            Avalonia.Automation.AutomationProperties.SetName(_billingTime,T("Firecrawl 帳單切換時間（台灣）","Firecrawl renewal time (Taiwan)"));
            panel.Children.Add(new TextBlock{Text=T("無本機點數上限。使用 API 查官方剩餘點數；額度不足時停用至設定的帳單日，到期重新核對。若尚未補點數，隔一小時再查。該月沒有設定日期時使用月底。","No local credit cap. Official credits are checked through the API. Insufficient credits pause this provider until your renewal date. If credits have not renewed, check again after an hour. Shorter months use their last day."),Foreground=Muted,FontSize=12,TextWrapping=TextWrapping.Wrap});
        }
        if(NativeSearch.UsesMonthlyRetry(id))panel.Children.Add(new TextBlock{Text=T("無本機額度上限。搜尋失敗後停用至下個月 2 號 00:05（台灣時間），期間改用備援；到期於下一次搜尋重試。","No local quota cap. Failed searches disable this provider until the 2nd of next month, 00:05 Taiwan time. Fallback providers are used until the next search retries it after that time."),Foreground=Muted,FontSize=12,TextWrapping=TextWrapping.Wrap});
        if(id!="exa")panel.Children.Add(ActionButton(T("查詢官方剩餘點數","Refresh official remaining credits"),()=>_search.RefreshUsageAsync(id,_lifetime.Token)));
        _content.Children.Add(new Border{Background=Surface,BorderBrush=Brush.Parse("#475457"),BorderThickness=new(1),CornerRadius=new(10),Padding=new(18),Child=panel});
        _rows[id]=(enabled,key,clear,state);
    }
    private Button ActionButton(string caption,Func<Task> action) {
        var button=new Button{Content=caption,Padding=new(16,9),HorizontalAlignment=HorizontalAlignment.Left};_buttons.Add(button);
        button.Click+=async(_,_)=>{
            if(_busy)return;SetBusy(true);_feedback.Text=T("處理中…","Working…");
            try{await action();await LoadAsync();_feedback.Text=T("已完成。設定立即生效。","Done. Changes take effect immediately.");}
            catch(OperationCanceledException){}catch(SearchFailure e){_feedback.Text=Reason(e.Code);}catch{_feedback.Text=T("無法完成，原資料保留。請檢查金鑰或網路。","Unable to complete. Existing data is preserved. Check the key and network.");}
            finally{if(!_lifetime.IsCancellationRequested)SetBusy(false);}
        };return button;
    }
    private void SetBusy(bool busy){_busy=busy;foreach(var b in _buttons)b.IsEnabled=!busy;_order.IsEnabled=!busy;_rotate.IsEnabled=!busy;_enabled.IsEnabled=!busy;foreach(var r in _rows.Values){r.Enabled.IsEnabled=!busy;r.Key.IsEnabled=!busy;r.Clear.IsEnabled=!busy;}_billingDay.IsEnabled=!busy;_billingTime.IsEnabled=!busy;}
    private async Task LoadAsync() {
        var status=await Task.Run(()=>_search.Status(),_lifetime.Token);
        _settings=(JsonObject)status["settings"]!.DeepClone();_enabled.IsChecked=J.B(_settings,"enabled");
        _rotate.IsChecked=J.S(_settings,"rotationMode")=="per-turn";
        var order=_settings["order"]!.AsArray().Select(n=>n!.GetValue<string>()).ToArray();_order.SelectedIndex=Array.FindIndex(Orders,o=>o.SequenceEqual(order));
        _billingDay.Value=J.N(_settings,"providers","firecrawl","billingDay");_billingTime.SelectedTime=TimeSpan.FromMinutes(J.N(_settings,"providers","firecrawl","billingMinute"));
        foreach(var p in status["providers"]!.AsArray()) {
            var id=J.S(p,"id");var r=_rows[id];r.Enabled.IsChecked=J.B(_settings,"providers",id,"enabled");
            var remaining=p!["officialRemaining"]?.ToJsonString()??T("未查詢","not checked");
            r.Status.Text=(J.B(p,"hasKey")?T("金鑰已設定","Key configured"):T("尚未設定金鑰","No key"))+"  ·  "+Reason(J.S(p,"reason"));
            if(NativeSearch.UsesMonthlyRetry(id)){
                r.Status.Text+="\n"+T("搜尋請求","Search requests")+" "+p!["requests"]+"  ·  "+T("無本機額度限制","No local quota cap");
                if(J.S(p,"reason")==id+"_failed")r.Status.Text+="\n"+T("原因：","Reason: ")+Reason(J.S(p,"failureReason"))+"\n"+T("恢復時間：","Retry after: ")+DateTimeOffset.FromUnixTimeMilliseconds(J.N(p,"disabledUntil")).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy/MM/dd HH:mm",System.Globalization.CultureInfo.InvariantCulture)+T("（台灣）"," (Taiwan)");
                if(id=="tavily")r.Status.Text+="\n"+T("本機點數用量","Local credits used")+" "+p!["used"]+"  ·  "+T("官方剩餘（僅供參考）","Official remaining (informational)")+" "+remaining;
            }else {
                r.Status.Text+="\n"+T("本機點數用量","Local credits used")+" "+p!["used"]+"  ·  "+T("無本機額度限制","No local quota cap")+"\n"+T("官方剩餘","Official remaining")+" "+remaining+(p!["officialRemaining"] is null?"":T(" 點"," credits"));
                r.Status.Text+="\n"+T("下次帳單切換：","Next renewal: ")+TaiwanDate(J.N(p,"nextBilling"));
                if(J.S(p,"reason")=="quota"&&J.N(p,"disabledUntil")>0)r.Status.Text+="\n"+T("重新核對時間：","Recheck after: ")+TaiwanDate(J.N(p,"disabledUntil"));
                if(J.N(p,"officialCheckedAt")>0)r.Status.Text+="\n"+T("餘額更新時間：","Balance checked: ")+TaiwanDate(J.N(p,"officialCheckedAt"));
            }
        }
    }
    private async Task SaveAsync() {
        if(_settings is null)return;
        var s=(JsonObject)_settings.DeepClone();s["enabled"]=_enabled.IsChecked==true;s["order"]=new JsonArray(Orders[Math.Max(0,_order.SelectedIndex)].Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());
        s["rotationMode"]=_rotate.IsChecked==true?"per-turn":"priority";
        var keys=new Dictionary<string,string>();var clear=new HashSet<string>();
        foreach(var(id,r)in _rows){s["providers"]![id]!["enabled"]=r.Enabled.IsChecked==true;if(r.Clear.IsChecked==true)clear.Add(id);var key=r.Key.Text?.Trim();if(!string.IsNullOrEmpty(key)){if(clear.Contains(id))throw new SearchFailure("invalid");keys[id]=key;}}
        s["providers"]!["firecrawl"]!["billingDay"]=_billingDay.Value??1;s["providers"]!["firecrawl"]!["billingMinute"]=(int)(_billingTime.SelectedTime??TimeSpan.FromMinutes(5)).TotalMinutes;
        await _search.ConfigureAsync(s,keys,clear,_lifetime.Token);
        foreach(var r in _rows.Values){r.Key.Text="";r.Clear.IsChecked=false;}
    }
    private static string TaiwanDate(long ms)=>DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy/MM/dd HH:mm",System.Globalization.CultureInfo.InvariantCulture)+T("（台灣）"," (Taiwan)");
    private static string Reason(string code)=>code switch {
        "exa_failed" or "tavily_failed"=>T("搜尋失敗，已停用","Search failed; disabled"),"timeout"=>T("搜尋逾時","Search timed out"),"ready"=>T("可用","Ready"),"missing_key"=>T("缺少金鑰","Missing key"),"disabled"=>T("已停用","Disabled"),"quota"=>T("官方點數不足，已停用","Insufficient official credits; paused"),"awaiting_balance"=>T("到期，等待核對官方餘額","Due; awaiting official balance check"),"key_budget"=>T("Exa 金鑰預算限制","Exa key budget reached"),"auth"=>T("金鑰無效","Invalid key"),"paid_plan"=>T("方案超出免費保護範圍","Plan exceeds free-tier protection"),"rate_limit"=>T("請求頻率受限","Request rate limited"),"invalid"=>T("設定或回覆格式不符，請檢查欄位。","Invalid settings or response. Check the fields."),"balance_not_supported"=>T("不支援查詢餘額","Balance lookup unsupported"),_=>T("暫時無法使用，稍後再試。","Temporarily unavailable. Retry later.")};
}
