using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EndfieldChargePlus.Assistant.Native;

namespace EndfieldChargePlus.Views;

public sealed class AssistantPersonaWindow:Window
{
    private readonly AssistantPersonaStore _store;
    private readonly Dictionary<string,AssistantPersona> _drafts=[];
    private readonly ObservableCollection<ListBoxItem> _items=[];
    private readonly List<Action> _translations=[];
    private readonly ListBox _list=new(){Name="PersonaList",Background=Brushes.Transparent,BorderThickness=new(0)};
    private readonly TextBox _name=new(){Name="PersonaName",MaxLength=60};
    private readonly TextBox _character=Editor("PersonaCharacter"),_rules=Editor("PersonaRules");
    private readonly TextBlock _active=new(){TextWrapping=TextWrapping.Wrap,FontSize=13,Foreground=Brush.Parse("#BFE6EA")};
    private readonly TextBlock _state=new(){FontSize=12,Foreground=Brush.Parse("#345D64"),TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _feedback=new(){TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=Brush.Parse("#345D64")};
    private readonly Grid _editors=new(){ColumnDefinitions=new("*,14,*")};
    private readonly StackPanel _closeWarning=new(){Spacing=8,IsVisible=false};
    private readonly Button _save,_activate,_delete;
    private string _selected=AssistantPersonaStore.DefaultId;
    private bool _loading,_refreshing,_discardClose,_deleteArmed,_closed;
    public AssistantPersonaWindow():this(NativeAssistantService.Shared.Personas){}
    internal AssistantPersonaWindow(AssistantPersonaStore store)
    {
        _store=store;Width=1080;Height=790;MinWidth=700;MinHeight=540;
        Background=Brush.Parse("#E3E9E7");Foreground=Brush.Parse("#202C2F");RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light;
        FontFamily=new("Segoe UI, Microsoft JhengHei UI");WindowStartupLocation=WindowStartupLocation.CenterScreen;
        foreach(var key in new[]{"SystemAccentColor","SystemAccentColorLight1","SystemAccentColorLight2","SystemAccentColorLight3","SystemAccentColorDark1","SystemAccentColorDark2","SystemAccentColorDark3"})Resources[key]=Color.Parse("#149AB2");
        ExtendClientAreaToDecorationsHint=true;ExtendClientAreaChromeHints=Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;ExtendClientAreaTitleBarHeightHint=48;
        var header=new Grid{Height=48,ColumnDefinitions=new("*,Auto"),Background=Brush.Parse("#252F32")};
        header.Children.Add(new TextBlock{Text="ENDFIELD  /  PERSONA LIBRARY",FontSize=12,Foreground=Brush.Parse("#91DDE9"),VerticalAlignment=VerticalAlignment.Center,Margin=new(22,0)});
        var close=ActionButton("關閉","Close",()=>Close());close.Content="×";close.Width=48;close.Height=48;close.Background=Brushes.Transparent;close.Foreground=Brushes.White;
        // The symbol stays constant; its accessible name follows the selected language.
        _translations.Add(()=>{close.Content="×";Avalonia.Automation.AutomationProperties.SetName(close,T("關閉人格設定","Close persona settings"));});
        Grid.SetColumn(close,1);header.Children.Add(close);
        header.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(header).Properties.IsLeftButtonPressed&&e.Source is not Button)BeginMoveDrag(e);};

        var sidebar=new Grid{RowDefinitions=new("Auto,Auto,*,Auto"),Background=Brush.Parse("#303B3E"),Margin=new(0)};
        var libraryTitle=Label("人格庫","LIBRARY",18);libraryTitle.Foreground=Brushes.White;libraryTitle.Margin=new(18,26,18,8);sidebar.Children.Add(libraryTitle);
        _active.Margin=new(18,0,18,20);Grid.SetRow(_active,1);sidebar.Children.Add(_active);
        _list.ItemsSource=_items;_list.Margin=new(8,0);Grid.SetRow(_list,2);sidebar.Children.Add(_list);
        _list.SelectionChanged+=(_,_)=>{if(!_refreshing&&_list.SelectedItem is ListBoxItem item&&item.Tag is string id)Select(id);};
        var additions=new StackPanel{Spacing=8,Margin=new(16,20)};
        var add=ActionButton("＋ 新增人格","＋ New persona",()=>NewDraft("","",""),"PersonaNew");
        var example=ActionButton("新增莊方宜範例","Zhuang Fangyi example",()=>NewDraft("莊方宜",AssistantPersonaPresets.Character,AssistantPersonaPresets.Rules),"PersonaExample");
        foreach(var action in new[]{add,example}){action.Background=Brush.Parse("#405155");action.Foreground=Brushes.White;action.BorderBrush=Brush.Parse("#667F85");action.BorderThickness=new(1);action.HorizontalAlignment=HorizontalAlignment.Stretch;additions.Children.Add(action);}
        var hint=Label("範例可自由修改，儲存後再啟用。","Edit the example, then save and activate.",11);hint.Foreground=Brush.Parse("#C6D5D7");additions.Children.Add(hint);
        Grid.SetRow(additions,3);sidebar.Children.Add(additions);

        var form=new StackPanel{Spacing=16};
        var intro=new StackPanel{Spacing=6};intro.Children.Add(Label("讓助理有自己的說話方式","Give your assistant a voice",28));
        intro.Children.Add(Label("人格決定語氣，互動規則決定怎麼與你相處。平常自然融入回答，只有你問起時才說明設定。","Personality shapes its voice; interaction rules shape how it responds to you. Settings stay in the background unless you ask about them.",13));form.Children.Add(intro);
        var nameGroup=new StackPanel{Spacing=7};nameGroup.Children.Add(Label("人格名稱","Persona name",12));nameGroup.Children.Add(_name);nameGroup.Children.Add(_state);form.Children.Add(nameGroup);
        _editors.Children.Add(EditorCard("01  /  角色人格","01  /  CHARACTER","性格、情感、背景與說話方式。","Temperament, emotional tone, background and voice.",_character));
        var rulesCard=EditorCard("02  /  互動規則","02  /  INTERACTION RULES","怎麼回應你，以及哪些事情不要做。","How to respond to you, and what to avoid.",_rules);Grid.SetColumn(rulesCard,2);_editors.Children.Add(rulesCard);form.Children.Add(_editors);
        form.Children.Add(Label("人格和記憶宮殿分開保存。切換從下一則訊息生效，搜尋、提醒與記憶流程照常運作。","Personas are separate from personal memory. Switching takes effect on the next message; search, reminders and memory continue as usual.",12));
        var scroll=new ScrollViewer{Content=form,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,Margin=new(28,24,28,12)};

        var footer=new StackPanel{Spacing=10,Margin=new(28,12,28,22)};
        var actions=new WrapPanel{Orientation=Orientation.Horizontal};
        _save=ActionButton("儲存人格","Save persona",SaveSelected,"PersonaSave");_save.Background=Brush.Parse("#147F94");_save.Foreground=Brushes.White;
        _activate=ActionButton("使用此人格","Use this persona",ActivateSelected,"PersonaActivate");_activate.Background=Brush.Parse("#DCE748");_activate.Foreground=Brush.Parse("#202C2F");
        _delete=ActionButton("刪除","Delete",DeleteSelected,"PersonaDelete");
        foreach(var button in new[]{_save,_activate,ActionButton("複製","Duplicate",()=>{var p=Current;NewDraft(DisplayName(p)+T(" 副本"," copy"),p.Character,p.Rules);},"PersonaDuplicate"),_delete}){button.Margin=new(0,0,8,6);actions.Children.Add(button);}
        footer.Children.Add(actions);footer.Children.Add(_feedback);
        _closeWarning.Children.Add(Label("還有未儲存草稿。先儲存，或選擇捨棄後關閉。","You have unsaved drafts. Save them, or discard them to close.",12));
        var closeActions=new WrapPanel();closeActions.Children.Add(ActionButton("捨棄草稿並關閉","Discard drafts and close",()=>{_discardClose=true;Close();},"PersonaDiscard"));
        closeActions.Children.Add(ActionButton("繼續編輯","Keep editing",()=>_closeWarning.IsVisible=false,"PersonaKeepEditing"));_closeWarning.Children.Add(closeActions);footer.Children.Add(_closeWarning);
        var editorArea=new Grid{RowDefinitions=new("*,Auto")};editorArea.Children.Add(scroll);Grid.SetRow(footer,1);editorArea.Children.Add(footer);
        var body=new Grid{ColumnDefinitions=new("220,*")};body.Children.Add(sidebar);Grid.SetColumn(editorArea,1);body.Children.Add(editorArea);
        var root=new Grid{RowDefinitions=new("48,*"),Background=Background};root.Children.Add(header);Grid.SetRow(body,1);root.Children.Add(body);Content=root;
        foreach(var input in new[]{_name,_character,_rules})input.PropertyChanged+=(_,e)=>{if(e.Property==TextBox.TextProperty)Edited();};
        SizeChanged+=(_,_)=>ResponsiveEditors();
        Closing+=(_,e)=>{if(!_discardClose&&_drafts.Values.Any(Dirty)){e.Cancel=true;_closeWarning.IsVisible=true;}};
        Closed+=(_,_)=>{_closed=true;LocalizationManager.LanguageChanged-=Translate;_store.Changed-=StoreChanged;_drafts.Clear();_loading=true;_name.Text=_character.Text=_rules.Text="";};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}else if(e.Key==Key.S&&e.KeyModifiers.HasFlag(KeyModifiers.Control)){if(_save.IsEnabled)_save.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));e.Handled=true;}};
        LocalizationManager.LanguageChanged+=Translate;_store.Changed+=StoreChanged;
        _translations.Add(()=>Title=T("AI 人格與互動規則","AI personality and interaction rules"));
        Translate();Select(AssistantPersonaStore.DefaultId);ResponsiveEditors();
    }
    private static string T(string zh,string en)=>LocalizationManager.Text(zh,en);
    private static TextBox Editor(string name)=>new(){Name=name,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxLength=AssistantPersonaStore.MaxField,Height=320,FontSize=14,LineHeight=23,VerticalContentAlignment=VerticalAlignment.Top,Padding=new(12)};
    private TextBlock Label(string zh,string en,double size){var label=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=size};_translations.Add(()=>label.Text=T(zh,en));return label;}
    private Button ActionButton(string zh,string en,Action action,string? name=null)
    {
        var button=new Button{Name=name,MinHeight=42,Padding=new(14,9),HorizontalContentAlignment=HorizontalAlignment.Center};_translations.Add(()=>button.Content=T(zh,en));
        button.Click+=(_,_)=>{try{action();}catch(Exception ex){_feedback.Text=ex.Message switch{"persona_invalid"=>T("請填寫名稱（最多 60 字）；每區最多 6,000 字，合計最多 10,000 字。","Enter a name (up to 60 characters); each field supports 6,000 characters, 10,000 combined."),"persona_limit"=>T("最多可儲存 30 組人格，請先刪除不使用的項目。","You can save 30 personas. Delete an unused one first."),"persona_changed"=>T("資料已被其他視窗修改。草稿保留，請重新開啟再試。","The library changed elsewhere. Your draft is kept; reopen before retrying."),_=>T("儲存未完成，草稿與原檔保留。請檢查檔案存取權限。","Could not save. Drafts and the original file are preserved. Check file access permissions.")};}};
        return button;
    }
    private Border EditorCard(string zh,string en,string helpZh,string helpEn,TextBox editor)
    {
        var panel=new StackPanel{Spacing=10};var title=Label(zh,en,15);title.Foreground=Brush.Parse("#17677A");panel.Children.Add(title);panel.Children.Add(Label(helpZh,helpEn,12));panel.Children.Add(editor);
        _translations.Add(()=>Avalonia.Automation.AutomationProperties.SetName(editor,T(zh,en)));
        return new Border{Child=panel,Padding=new(16),Background=Brush.Parse("#F5F7F4"),BorderBrush=Brush.Parse("#A8BBBE"),BorderThickness=new(1),CornerRadius=new(6)};
    }
    private AssistantPersona Current=>_drafts.TryGetValue(_selected,out var draft)?draft:_store.Profiles.First(p=>p.Id==_selected);
    private static string DisplayName(AssistantPersona p)=>p.Id==AssistantPersonaStore.DefaultId?T("預設助理","Default assistant"):p.Name;
    private bool Dirty(AssistantPersona p)=>_store.Profiles.FirstOrDefault(saved=>saved.Id==p.Id)!=p;
    private void Edited()
    {
        if(_loading||_selected==AssistantPersonaStore.DefaultId)return;
        _drafts[_selected]=new(_selected,_name.Text??"",_character.Text??"",_rules.Text??"");_deleteArmed=false;RefreshState();RefreshList();
    }
    private void Select(string id)
    {
        _selected=id;_loading=true;var p=Current;_name.Text=p.Id==AssistantPersonaStore.DefaultId?DisplayName(p):p.Name;_character.Text=p.Character;_rules.Text=p.Rules;_loading=false;_deleteArmed=false;_feedback.Text="";RefreshState();RefreshList();
    }
    private void NewDraft(string name,string character,string rules)
    {
        var draft=new AssistantPersona(Guid.NewGuid().ToString("N"),name,character,rules);_drafts[draft.Id]=draft;Select(draft.Id);_name.Focus();
    }
    private void SaveSelected()
    {
        if(_selected==AssistantPersonaStore.DefaultId)return;
        var draft=Current;var saved=_store.Save(draft.Id,draft.Name,draft.Character,draft.Rules);_drafts.Remove(saved.Id);Select(saved.Id);
        _feedback.Text=_store.Active.Id==saved.Id?T("已加密儲存；正在使用的設定從下一則訊息更新。","Encrypted and saved. The active persona updates on your next message."):T("已加密儲存。按「使用此人格」即可切換。","Encrypted and saved. Choose Use this persona to switch.");
    }
    private void ActivateSelected()
    {
        if(Dirty(Current)){_feedback.Text=T("請先儲存這份草稿，再切換使用。","Save this draft before activating it.");return;}
        _store.Activate(_selected);_feedback.Text=T("已切換，下一則訊息開始使用。","Switched. Applies to your next message.");
    }
    private void DeleteSelected()
    {
        if(_selected==AssistantPersonaStore.DefaultId)return;
        if(!_deleteArmed){_deleteArmed=true;_feedback.Text=T("再按一次刪除以確認。刪除使用中的人格會回到預設助理。","Press Delete again to confirm. Deleting the active persona restores the default assistant.");return;}
        var id=_selected;if(_store.Profiles.Any(p=>p.Id==id))_store.Delete(id);_drafts.Remove(id);Select(AssistantPersonaStore.DefaultId);_feedback.Text=T("人格已刪除。","Persona deleted.");
    }
    private void RefreshList()
    {
        _refreshing=true;
        var saved=_store.Profiles;
        var profiles=saved.Select(p=>_drafts.GetValueOrDefault(p.Id,p)).Concat(_drafts.Values.Where(d=>!saved.Any(p=>p.Id==d.Id))).ToArray();
        var rebuild=!_items.Select(item=>item.Tag as string).SequenceEqual(profiles.Select(p=>p.Id));
        if(rebuild){_list.SelectedItem=null;_items.Clear();}
        foreach(var p in profiles){
            var panel=new StackPanel{Spacing=4,Margin=new(6,8)};panel.Children.Add(new TextBlock{Text=string.IsNullOrWhiteSpace(p.Name)&&p.Id!=AssistantPersonaStore.DefaultId?T("未命名人格","Untitled persona"):DisplayName(p),TextWrapping=TextWrapping.Wrap,FontSize=14,Foreground=Brushes.White});
            panel.Children.Add(new TextBlock{Text=_store.Active.Id==p.Id?T("使用中","ACTIVE")+(Dirty(p)?T(" · 草稿"," · DRAFT"):""):Dirty(p)?T("未儲存草稿","UNSAVED DRAFT"):T("已儲存","SAVED"),Foreground=_store.Active.Id==p.Id?Brush.Parse("#DEED6A"):Brush.Parse("#B5CDCF"),FontSize=10});
            ListBoxItem item;
            if(rebuild){item=new(){Content=panel,Tag=p.Id,HorizontalContentAlignment=HorizontalAlignment.Stretch};_items.Add(item);}
            else{item=_items.First(item=>item.Tag as string==p.Id);item.Content=panel;}
            item.Background=p.Id==_selected?Brush.Parse("#20596A"):Brushes.Transparent;
            item.BorderBrush=Brush.Parse("#7CDCE9");item.BorderThickness=p.Id==_selected?new Thickness(2,0,0,0):new Thickness(0);
            if(p.Id==_selected)_list.SelectedItem=item;
        }
        _refreshing=false;
    }
    private void RefreshState()
    {
        var p=Current;var immutable=p.Id==AssistantPersonaStore.DefaultId;var dirty=Dirty(p);
        _name.IsReadOnly=_character.IsReadOnly=_rules.IsReadOnly=immutable;
        _save.IsEnabled=dirty&&!immutable&&!_store.StorageError;_activate.IsEnabled=!dirty&&!_store.StorageError&&_store.Active.Id!=p.Id;_delete.IsEnabled=!immutable&&!_store.StorageError;
        _activate.Content=_store.Active.Id==p.Id?T("使用中","Active"):T("使用此人格","Use this persona");
        _state.Text=immutable?T("原本的自然助理。可複製後建立自己的版本。","The original natural assistant. Duplicate it to create your own version."):dirty?T("未儲存 · 切換列表會保留草稿","Unsaved · Draft stays when selecting another persona"):T("已儲存 · 可隨時修改並重新儲存","Saved · Edit and save whenever you like");
        _active.Text=T("目前使用：","Currently using: ")+DisplayName(_store.Active);
        if(_store.StorageError)_feedback.Text=T("人格檔案無法讀取，原檔已保留。目前使用預設助理。","Unable to read the persona library. Original file preserved; using the default assistant.");
    }
    private void StoreChanged()=>Avalonia.Threading.Dispatcher.UIThread.Post(()=>{if(!_closed){RefreshList();RefreshState();}});
    private void Translate(){foreach(var action in _translations)action();_feedback.Text="";_deleteArmed=false;if(_selected==AssistantPersonaStore.DefaultId){_loading=true;_name.Text=DisplayName(Current);_loading=false;}RefreshList();RefreshState();}
    private void ResponsiveEditors()
    {
        var compact=Width<980;_editors.ColumnDefinitions=new(compact?"*":"*,14,*");_editors.RowDefinitions=new(compact?"Auto,14,Auto":"Auto");
        Grid.SetColumn(_editors.Children[1],compact?0:2);Grid.SetRow(_editors.Children[1],compact?2:0);
        _character.Height=_rules.Height=compact?230:320;
    }
}
