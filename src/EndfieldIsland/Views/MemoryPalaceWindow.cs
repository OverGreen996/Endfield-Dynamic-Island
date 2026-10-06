using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EndfieldChargePlus.Assistant;
using System.Globalization;

namespace EndfieldChargePlus.Views;

/// <summary>Chinese, editable local records. This window never calls the AI/search hub.</summary>
public sealed class MemoryPalaceWindow : Window
{
    private readonly PersonalAssistantStore _store;
    private readonly StackPanel _memories=new(){Spacing=12},_reminders=new(){Spacing=12};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#EAEF24")};
    private readonly TextBlock _intro=new(){TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Brush.Parse("#B4BDB7")};
    private readonly TabItem _memoryTab=new(),_reminderTab=new();
    private static readonly IBrush White=Brush.Parse("#F0F1ED");
    private bool _dirty,_refreshPending,_sized;
    private string? _statusMessage;
    private readonly List<Action> _localizeDetails=new();
    public MemoryPalaceWindow(PersonalAssistantStore? store=null)
    {
        _store=store??PersonalAssistantStore.Shared;
        Title="記憶宮殿與定時提醒";Width=760;Height=650;MinWidth=380;MinHeight=320;
        Background=Brush.Parse("#191D1D");RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;Classes.Add("industrial");
        var heading=new TextBlock{Text="記憶宮殿",FontSize=22,Foreground=White};
        var intro=new StackPanel{Spacing=6,Margin=new Thickness(0,8,0,14)};
        intro.Children.Add(_intro);
        intro.Children.Add(_status);
        _memoryTab.Content=Scroll(_memories);_reminderTab.Content=Scroll(_reminders);
        var tabs=new TabControl{HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,HorizontalContentAlignment=HorizontalAlignment.Stretch,ItemsSource=new[]{_memoryTab,_reminderTab}};
        var root=new PalaceLayoutGrid(tabs,heading,intro,_memories,_reminders){Background=Background,Margin=new Thickness(20),ColumnDefinitions=new ColumnDefinitions("*"),RowDefinitions=new RowDefinitions("Auto,Auto,*")};
        root.Children.Add(heading);Grid.SetRow(intro,1);root.Children.Add(intro);
        Grid.SetRow(tabs,2);root.Children.Add(tabs);Content=root;
        _store.Changed+=RequestRefresh;LocalizationManager.LanguageChanged+=ApplyLanguage;Closed+=(_,_)=>{_store.Changed-=RequestRefresh;LocalizationManager.LanguageChanged-=ApplyLanguage;};
        Opened+=(_,_)=>{if(!_sized){_sized=true;var area=Screens.Primary?.WorkingArea;var scale=Screens.Primary?.Scaling??1;if(area is {}a){Width=Math.Min(760,a.Width/scale-32);Height=Math.Min(650,a.Height/scale-48);MinWidth=Math.Min(MinWidth,Width);MinHeight=Math.Min(MinHeight,Height);}}if(_refreshPending&&!_dirty)Refresh();};
        Refresh();
    }
    private void ApplyLanguage()
    {
        LocalizationManager.ApplyStaticText(this);UpdateStatus();
        _memoryTab.Header=LocalizationManager.Text("個人記憶","Personal memory");
        _reminderTab.Header=LocalizationManager.Text("定時提醒","Reminders");
        _intro.Text=LocalizationManager.Text(
            "AI 理解對話後決定是否記憶，並建立合適的中文分類，不需要特定口令。你可以逐筆修改文字、分類或刪除。\n提醒由 AI 理解時間與事項，到點後在本機執行。刪除記憶不會刪掉聊天紀錄；若不想沿用舊聊天內容，請開新對話。",
            "AI understands your conversation, decides what to remember and creates suitable Chinese categories. No command phrases are required. Edit or delete any entry or category.\nAI interprets reminder requests; the app delivers reminders locally. Deleting memories keeps chat history. Start a new conversation to stop using old chat context.");
        foreach(var update in _localizeDetails)update();
    }
    private static ScrollViewer Scroll(Control content)=>new(){Content=content,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto};
    private TextBlock Label(string text)
    {
        var label=new TextBlock{Foreground=White,TextWrapping=TextWrapping.Wrap,FontSize=12};
        void Update()=>label.Text=LocalizationManager.TranslateLiteral(text);
        _localizeDetails.Add(Update);Update();return label;
    }
    private Button LocalButton(string text,Thickness margin=default)
    {
        var button=new Button{Margin=margin};
        void Update()=>button.Content=LocalizationManager.TranslateLiteral(text);
        _localizeDetails.Add(Update);Update();return button;
    }
    private static string SourceText(string source)
    {
        const string prefix="AI 理解本次對話 · 原文：";
        return LocalizationManager.IsEnglish&&source.StartsWith(prefix,StringComparison.Ordinal)
            ?"AI understood this conversation · Original quote: "+source[prefix.Length..]
            :LocalizationManager.TranslateLiteral(source);
    }
    private static Border Card(Control child)=>new(){Child=child,Padding=new Thickness(14),CornerRadius=new CornerRadius(10),Background=Brush.Parse("#252B2A"),BorderBrush=Brush.Parse("#505A54"),BorderThickness=new Thickness(1)};
    private void UpdateStatus()=>_status.Text=_statusMessage is {}message?LocalizationManager.TranslateLiteral(message):_store.StorageError??LocalizationManager.Text($"個人記憶 {_store.Memories.Count}/200 筆 · 提醒 {_store.Reminders.Count}/300 筆 · 本機加密保存",$"Memory {_store.Memories.Count}/200 · Reminders {_store.Reminders.Count}/300 · Encrypted locally");
    private void RequestRefresh(){_refreshPending=true;if(!_dirty&&IsVisible)Refresh();else if(_dirty){_statusMessage="資料已更新；保留你的編輯草稿，請儲存修改。";UpdateStatus();}}
    private void Track(TextBox box)=>box.TextChanged+=(_,_)=>_dirty=true;
    private void Act(Action action){try{action();_dirty=false;Refresh();_statusMessage="已儲存。";UpdateStatus();}catch(Exception ex){_statusMessage=ex.Message;UpdateStatus();}}
    public void Refresh()
    {
        _refreshPending=false;
        _localizeDetails.Clear();
        _statusMessage=null;UpdateStatus();
        _memories.Children.Clear();_reminders.Children.Clear();
        if (_store.Memories.Count == 0) {
            var empty=Label("");void UpdateEmpty()=>empty.Text=LocalizationManager.Text("目前沒有記憶。AI 會理解你的對話，自動建立合適的中文分類。","No memories yet. AI will understand your conversation and create suitable Chinese categories.");
            _localizeDetails.Add(UpdateEmpty);UpdateEmpty();_memories.Children.Add(empty);
        }
        foreach(var category in _store.MemoryCategories)
        {
            var group=_store.Memories.Where(m=>m.Category==category).OrderByDescending(m=>m.Saved).ToArray();
            var heading=new TextBlock{FontSize=15,Foreground=Brush.Parse("#EAEF24"),Margin=new Thickness(0,8,0,0)};void UpdateHeading()=>heading.Text=$"{category} ({group.Length})";_localizeDetails.Add(UpdateHeading);UpdateHeading();_memories.Children.Add(heading);
            if(group.Length==0){_memories.Children.Add(Label("目前沒有內容"));continue;}
            foreach(var memory in group)
            {
                var panel=new StackPanel{Spacing=8};
                var editor=new TextBox{Text=memory.Text,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxLength=500,MinHeight=38};
                Track(editor);
                panel.Children.Add(editor);var sourceLabel=Label("");void UpdateSource()=>sourceLabel.Text=LocalizationManager.Text($"來源：{memory.Source} · {memory.Saved.LocalDateTime:yyyy/MM/dd HH:mm} · 編號 {memory.Id}",$"Source: {SourceText(memory.Source)} · {memory.Saved.LocalDateTime:yyyy/MM/dd HH:mm} · ID {memory.Id}");_localizeDetails.Add(UpdateSource);UpdateSource();panel.Children.Add(sourceLabel);
                var actions=new WrapPanel{Orientation=Orientation.Horizontal};
                var categoryLabel=Label("");panel.Children.Add(categoryLabel);
                var kind=new TextBox{Text=memory.Category,Width=160,MaxLength=24,Watermark=LocalizationManager.Text("中文分類","Chinese category")};Track(kind);
                void UpdateCategoryLabel(){categoryLabel.Text=LocalizationManager.Text("分類（AI 自動建立，也可自行修改）","Category (created by AI; editable)");kind.Watermark=LocalizationManager.Text("中文分類","Chinese category");Avalonia.Automation.AutomationProperties.SetName(kind,LocalizationManager.Text("記憶分類","Memory category"));}
                _localizeDetails.Add(UpdateCategoryLabel);UpdateCategoryLabel();
                var save=LocalButton("儲存修改",new Thickness(8,0));save.Click+=(_,_)=>Act(()=>_store.UpdateMemory(memory.Id,kind.Text?.Trim()??memory.Category,editor.Text??""));
                var delete=LocalButton("刪除此記憶");delete.Click+=(_,_)=>Act(()=>_store.DeleteMemory(memory.Id));
                actions.Children.Add(kind);actions.Children.Add(save);actions.Children.Add(delete);panel.Children.Add(actions);_memories.Children.Add(Card(panel));
            }
        }
        _reminders.Children.Add(Label("軟體運行時提醒；睡眠／關機期間不響，回來後補顯示逾期事項。最多 300 筆，滿額依建立順序移除最舊提醒並取消排程。提醒只在本機，不同步 Windows 行事曆。"));
        if(_store.Reminders.Count==0)_reminders.Children.Add(Label("尚未建立提醒。對助理說：明天上午九點提醒我開會。"));
        foreach(var reminder in _store.Reminders.OrderBy(r=>r.Displayed is not null).ThenBy(r=>r.Due))
        {
            var panel=new StackPanel{Spacing=8};var reminderLabel=Label("");
            void UpdateReminderLabel()=>reminderLabel.Text=LocalizationManager.Text($"{(reminder.Displayed is null?"待提醒":"已顯示")} · 編號 {reminder.Id}"+(reminder.Created==default?"":$" · 建立 {reminder.Created.LocalDateTime:yyyy/MM/dd HH:mm}"),$"{(reminder.Displayed is null?"Pending":"Shown")} · ID {reminder.Id}"+(reminder.Created==default?"":$" · Created {reminder.Created.LocalDateTime:yyyy/MM/dd HH:mm}"));
            _localizeDetails.Add(UpdateReminderLabel);UpdateReminderLabel();panel.Children.Add(reminderLabel);
            var text=new TextBox{Text=reminder.Text,TextWrapping=TextWrapping.Wrap,MaxLength=500};
            var date=new TextBox{Text=reminder.Due.LocalDateTime.ToString("yyyy/MM/dd HH:mm"),Watermark="yyyy/MM/dd HH:mm"};
            Track(text);Track(date);
            panel.Children.Add(Label("提醒事項"));panel.Children.Add(text);panel.Children.Add(Label("提醒時間（本機時區）"));panel.Children.Add(date);
            var actions=new WrapPanel();var save=LocalButton("修改並重新排程",new Thickness(0,0,8,0));
            save.Click+=(_,_)=>Act(()=>{if(!DateTime.TryParseExact(date.Text,"yyyy/MM/dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var local)||TimeZoneInfo.Local.IsInvalidTime(local)||TimeZoneInfo.Local.IsAmbiguousTime(local))throw new InvalidOperationException("時間格式請填 yyyy/MM/dd HH:mm。");_store.UpdateReminder(reminder.Id,text.Text??"",new DateTimeOffset(local,TimeZoneInfo.Local.GetUtcOffset(local)));});
            var delete=LocalButton("刪除此提醒");delete.Click+=(_,_)=>Act(()=>_store.DeleteReminder(reminder.Id));actions.Children.Add(save);actions.Children.Add(delete);panel.Children.Add(actions);_reminders.Children.Add(Card(panel));
        }
        ApplyLanguage();
    }

    private sealed class PalaceLayoutGrid(TabControl tabs,TextBlock heading,StackPanel intro,StackPanel memories,StackPanel reminders):Grid
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            var width=double.IsFinite(availableSize.Width)?availableSize.Width:720;
            tabs.Width=tabs.MaxWidth=Math.Max(1,width);
            memories.MaxWidth=reminders.MaxWidth=Math.Max(1,width-28);
            heading.Measure(new Size(width,double.PositiveInfinity));
            intro.Measure(new Size(width,double.PositiveInfinity));
            tabs.MaxHeight=double.IsFinite(availableSize.Height)
                ?Math.Max(40,availableSize.Height-heading.DesiredSize.Height-intro.DesiredSize.Height)
                :double.PositiveInfinity;
            return base.MeasureOverride(availableSize);
        }
    }
}
