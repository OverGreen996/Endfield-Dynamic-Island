using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Diagnostics;
using EndfieldChargePlus.Views;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Music;
using Avalonia;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Automation;

namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow : Window
{
    private readonly AssistantClient _assistantClient = new();
    private CancellationTokenSource? _geminiKeyRequest;
    private bool _savingGeminiKey, _geminiSettingsClosed;
    public event Action? AssistantRequested;
    public event Action<MusicMode?>? MusicRequested;
    private readonly EndfieldChargePlus.Music.PlaylistStore _playlists = new();
    public void SelectMusicTab() => MusicTab.IsSelected = true;
    private bool SaveMusicPlaylist()
    {
        try { var url = EndfieldChargePlus.Music.PlaylistStore.Normalize(MusicPlaylistInput.Text); _playlists.Save(url); MusicPlaylistInput.Text = url; MusicSettingsNotice.Text = LocalizationManager.TranslateLiteral("播放清單已儲存。"); return true; }
        catch (Exception ex) { MusicSettingsNotice.Text = ex.Message; return false; }
    }
    private static readonly HttpClient UpdateHttpClient = CreateUpdateHttpClient();
    private static UpdateCheckResult? _lastUpdateResult;

    public enum UpdateStatusKind
    {
        NoRemoteVersion,
        Uncomparable,
        UpdateAvailable,
        UpToDate,
        LocalNewer,
        NetworkError,
        Timeout,
        Error,
    }

    public sealed record UpdateCheckResult(
        string CurrentVersion,
        string? LatestVersion,
        UpdateStatusKind Status,
        bool HasUpdate,
        bool CheckSucceeded,
        string? Detail = null)
    {
        public string LatestVersionText => Status switch
        {
            UpdateStatusKind.NoRemoteVersion => LocalizationManager.Text("最新版本：尚未取得", "Latest: unavailable"),
            UpdateStatusKind.NetworkError or UpdateStatusKind.Error => LocalizationManager.Text("最新版本：檢查失敗", "Latest: check failed"),
            UpdateStatusKind.Timeout => LocalizationManager.Text("最新版本：檢查逾時", "Latest: timed out"),
            _ when !string.IsNullOrWhiteSpace(LatestVersion) => LocalizationManager.Text($"最新版本：v{LatestVersion}", $"Latest: v{LatestVersion}"),
            _ => LocalizationManager.Text("最新版本：尚未取得", "Latest: not checked"),
        };

        public string StatusText => Status switch
        {
            UpdateStatusKind.NoRemoteVersion => LocalizationManager.Text("狀態：尚未取得 Release / 標籤，儲存庫可能尚未公開", "Status: no release/tag found; the repository may not be public yet"),
            UpdateStatusKind.Uncomparable => LocalizationManager.Text("狀態：已取得線上版本，但版本編號格式無法比較", "Status: remote version found, but its format cannot be compared"),
            UpdateStatusKind.UpdateAvailable => LocalizationManager.Text("狀態：發現新版本", "Status: update available"),
            UpdateStatusKind.UpToDate => LocalizationManager.Text("狀態：目前已是最新版本", "Status: up to date"),
            UpdateStatusKind.LocalNewer => LocalizationManager.Text("狀態：目前版本高於線上最新版本", "Status: local version is newer than the latest online version"),
            UpdateStatusKind.NetworkError => LocalizationManager.Text($"狀態：網路請求失敗（{Detail ?? "連線錯誤"}）", $"Status: network request failed ({Detail ?? "connection error"})"),
            UpdateStatusKind.Timeout => LocalizationManager.Text("狀態：檢查逾時，請稍後再試", "Status: update check timed out; try again later"),
            _ => LocalizationManager.Text("狀態：檢查更新時發生錯誤", "Status: an error occurred while checking for updates"),
        };
    }

    public static UpdateCheckResult? LastUpdateResult => _lastUpdateResult;

    private AppSettings _settings;
    private AiBackupSettingsWindow? _aiBackups;
    private AssistantPersonaWindow? _personas;
    private GeminiPoolWindow? _geminiPoolWindow;
    private readonly HudWindow _hud;
    private readonly CustomHudRuntime _runtime;
    private readonly Action<AppSettings> _saveSettings;
    private readonly Action<bool> _applyStartup;
    private bool _monitorComboReady;
    private string? _maintenanceStatusZh;
    private string? _maintenanceStatusEn;

    public SettingsWindow(AppSettings settings, HudWindow hud, CustomHudRuntime runtime, Action<AppSettings>? saveSettings = null, Action<bool>? applyStartup = null)
    {
        InitializeComponent();
        _settings = settings;
        _hud = hud;
        _runtime = runtime;
        Customizer.UseDataService(runtime.Data);
        _saveSettings = saveSettings ?? SettingsManager.Save;
        _applyStartup = applyStartup ?? StartupManager.Apply;

        LocalizationManager.ApplyStaticText(this);
        RebuildLocalizedChoiceItems();
        InitializeWindowChrome();

        LanguageChineseBtn.Click += (_, _) => OnLanguageSelected(AppLanguage.TraditionalChinese);
        LanguageEnglishBtn.Click += (_, _) => OnLanguageSelected(AppLanguage.English);

        ApplySettingsToControls(settings);
        int moduleIndex=0;
        foreach(var tab in ModuleTabs.Items.OfType<TabItem>())
        {
            int index=moduleIndex++;string[] icons={"terminal","shield","bolt","monitor","activity","link"};
            tab.HeaderTemplate=new FuncDataTemplate<string>((caption,_)=>
            {
                var header=new Grid{ColumnDefinitions=new ColumnDefinitions("Auto,20,*"),MinHeight=30};
                var number=new TextBlock{Text=$"{index+1:00}",FontFamily=new FontFamily("Consolas"),FontSize=10,Margin=new Thickness(0,0,8,0),Foreground=Brush.Parse("#A3B3B5"),VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center,Tag="module-caption"};
                var icon=new PathIcon{Width=20,Height=20,Data=IconCatalog.GetGeometry(icons[Math.Min(index,icons.Length-1)])};
                if(index==2)icon.Data=Geometry.Parse("M 6,4 L 19,2 L 19,17 C 19,23 11,24 11,19 C 11,17 15,15 17,16 L 17,6 L 8,8 L 8,20 C 8,26 0,27 0,22 C 0,20 4,18 6,19 Z");
                var label=new TextBlock{Text=caption,FontSize=13,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center,Margin=new Thickness(8,0,0,0),Tag="module-caption",IsVisible=Width>=760};
                label.Bind(TextBlock.ForegroundProperty,new Binding(nameof(TabItem.Foreground)){Source=tab});
                icon.Bind(PathIcon.ForegroundProperty,new Binding(nameof(TabItem.Foreground)){Source=tab});
                Grid.SetColumn(icon,1);Grid.SetColumn(label,2);header.Children.Add(number);header.Children.Add(icon);header.Children.Add(label);ToolTip.SetTip(header,caption);return header;
            });
        }
        void SetNavigation()
        {
            double viewport=SettingsRoot.Bounds.Width>0?SettingsRoot.Bounds.Width:Width;
            if(SettingsRoot.Bounds.Width>0 && SettingsRoot.ColumnDefinitions[0].Width!=new GridLength(viewport))SettingsRoot.ColumnDefinitions[0].Width=new GridLength(viewport);
            ModuleTabs.Width=viewport;
            bool compact=viewport<760;ModuleTabs.TabStripPlacement=Dock.Left;ModuleTabs.Classes.Set("settingsCompact",compact);
            var layout=ModuleTabs.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g=>g.Name=="SettingsNavigationLayout");
            if(layout is not null && layout.ColumnDefinitions[0].Width.Value!=(compact?76:204))layout.ColumnDefinitions[0].Width=new GridLength(compact?76:204);
            foreach(var brand in ModuleTabs.GetVisualDescendants().OfType<StackPanel>().Where(p=>p.Name is "SettingsNavigationBrand" or "SettingsNavigationFooter"))brand.IsVisible=!compact;
            foreach(var label in ModuleTabs.GetVisualDescendants().OfType<TextBlock>().Where(t=>t.Tag as string=="module-caption"))label.IsVisible=!compact;
        }
        SizeChanged+=(_,_)=>SetNavigation();SettingsRoot.SizeChanged+=(_,_)=>Avalonia.Threading.Dispatcher.UIThread.Post(SetNavigation);SetNavigation();
        ModuleTabs.SelectionChanged += (_, _) => UpdateWindowChrome();
        UpdateLanguageSwitchVisual();

        HudEnabledSwitch.IsCheckedChanged += (_, _) => UpdateModeUi();
        AlwaysVisibleSwitch.IsCheckedChanged += (_, _) => UpdateModeUi();
        ShowClockSwitch.IsCheckedChanged += (_, _) => UpdateModeUi();
        PositionModeCombo.SelectionChanged += (_, _) => UpdateModeUi();
        SaveBtn.Click += OnSave;
        InitializeNotificationSettings();
        OpenAssistantBtn.Click += (_, _) => AssistantRequested?.Invoke();
        ChooseUserAvatarBtn.Click+=async(_,_)=>await ChooseAvatarAsync(true);
        ChooseAiAvatarBtn.Click+=async(_,_)=>await ChooseAvatarAsync(false);
        ResetAvatarsBtn.Click+=(_,_)=>{try{AvatarStore.Shared.Reset();AvatarNotice.Text=LocalizationManager.Text("已還原預設頭像。","Default avatars restored.");}catch{AvatarNotice.Text=LocalizationManager.Text("無法還原頭像，請稍後再試。","Avatars could not be reset. Try again later.");}};
        try { MusicPlaylistInput.Text = _playlists.Load(); }
        catch (Exception ex) { MusicSettingsNotice.Text = LocalizationManager.TranslateLiteral("既有清單設定無法讀取，未覆寫：") + ex.Message; }
        SaveMusicPlaylistBtn.Click += (_, _) => SaveMusicPlaylist();
        OpenMusicPlaylistBtn.Click += (_, _) => { if (SaveMusicPlaylist()) MusicRequested?.Invoke(MusicMode.Playlist); };
        FollowBrowserMusicBtn.Click += (_, _) => MusicRequested?.Invoke(MusicMode.Browser);
        ShowMusicBtn.Click += (_, _) => MusicRequested?.Invoke(null);
        void RefreshKeySubmit() => SaveGeminiKeyBtn.IsEnabled = !_savingGeminiKey && GeminiFreeConfirmation.IsChecked == true && !string.IsNullOrWhiteSpace(GeminiKeyInput.Text);
        GeminiKeyInput.TextChanged += (_, _) => RefreshKeySubmit();
        GeminiFreeConfirmation.IsCheckedChanged += (_, _) => RefreshKeySubmit();
        SaveGeminiKeyBtn.Click += async (_, _) => await SaveGeminiKeyAsync();
        OpenAiBackupsBtn.Click+=(_,_)=>{
            try{if(_aiBackups is null){_aiBackups=new AiBackupSettingsWindow();_aiBackups.Closed+=(_,_)=>_aiBackups=null;}_aiBackups.Show();_aiBackups.Activate();}
            catch{GeminiKeyStatusText.Text=LocalizationManager.Text("AI 備援設定無法開啟，請檢查本機資料。","Cannot open AI fallback settings. Check local data.");}
        };
        Closed+=(_,_)=>_aiBackups?.Close();
        OpenGeminiPoolBtn.Click+=(_,_)=>{
            try{if(_geminiPoolWindow is null){_geminiPoolWindow=new GeminiPoolWindow();_geminiPoolWindow.Closed+=(_,_)=>_geminiPoolWindow=null;}_geminiPoolWindow.Show();_geminiPoolWindow.Activate();}
            catch{GeminiKeyStatusText.Text=LocalizationManager.Text("Gemini 輪換設定暫時無法開啟。","Gemini rotation settings could not be opened.");}
        };
        Closed+=(_,_)=>_geminiPoolWindow?.Close();
        OpenPersonasBtn.Click+=(_,_)=>{
            try{if(_personas is null){_personas=new AssistantPersonaWindow();_personas.Closed+=(_,_)=>_personas=null;}_personas.Show();_personas.Activate();}
            catch{GeminiKeyStatusText.Text=LocalizationManager.Text("人格設定暫時無法開啟。","Persona settings could not be opened.");}
        };
        RefreshGeminiUsageBtn.Click += async (_, _) => await RefreshGeminiUsageAsync();
        Opened += async (_, _) => await RefreshGeminiUsageAsync();
        Closed += (_, _) => { _geminiSettingsClosed = true; GeminiKeyInput.Text = ""; _geminiKeyRequest?.Cancel(); _assistantClient.Dispose(); };
        OpenSettingsFolderBtn.Click += OnOpenSettingsFolder;
        ResetSizeAnimationBtn.Click += OnResetSizeAnimation;
        ExportSettingsBtn.Click += OnExportSettings;
        ImportSettingsBtn.Click += OnImportSettings;
        BackupSettingsBtn.Click += OnBackupSettings;
        OpenLogsFolderBtn.Click += OnOpenLogsFolder;
        ResetAllSettingsBtn.Click += OnResetAllSettings;
        HudOpacitySlider.ValueChanged += (_, _) => UpdateOpacityLabel();
        OpenProjectGithubBtn.Click += (_, _) => OpenUrl("https://github.com/OverGreen996/Endfield-Dynamic-Island");
        OpenUpstreamBtn.Click += (_, _) => OpenUrl("https://github.com/QinAnze/zmd-charge");
        OpenWebsiteBtn.Click += (_, _) => OpenUrl("https://github.com/OverGreen996/Endfield-Dynamic-Island/releases");
        CheckUpdateBtn.Click += OnCheckUpdate;

        AboutArchitectureText.Text = $"Windows · {GetCurrentProcessArchitectureText()}";

        string currentVersion = GetCurrentVersionText();
        AboutVersionText.Text = $"v{currentVersion}";
        UpdateCurrentVersionText.Text = LocalizationManager.Text($"目前版本：v{currentVersion}", $"Current: v{currentVersion}");
        UpdateLatestVersionText.Text = LocalizationManager.Text("最新版本：尚未取得", "Latest: not checked");
        UpdateStatusText.Text = LocalizationManager.Text("狀態：尚未檢查", "Status: not checked");
        if (LastUpdateResult is { } cachedUpdateResult)
            ApplyUpdateCheckResult(cachedUpdateResult);

        Opened += (_, _) =>
        {
            PopulateMonitorCombo();
            var screen=Screens.ScreenFromWindow(this)??Screens.Primary;
            if(screen is null)return;
            double scale=screen.Scaling>0?screen.Scaling:1;
            double availableWidth=Math.Max(240,screen.WorkingArea.Width/scale-32),availableHeight=Math.Max(180,screen.WorkingArea.Height/scale-60);
            MinWidth=Math.Min(940,availableWidth);MinHeight=Math.Min(680,availableHeight);
            Width=Math.Min(1120,availableWidth);Height=Math.Min(790,availableHeight);
            Position=new Avalonia.PixelPoint(screen.WorkingArea.X+(int)((screen.WorkingArea.Width-Width*scale)/2),screen.WorkingArea.Y+(int)Math.Max(8*scale,(screen.WorkingArea.Height-Height*scale)/2));
        };

        UpdateModeUi();
    }
    private async Task ChooseAvatarAsync(bool user)
    {
        var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title=LocalizationManager.Text("選擇大頭照","Choose avatar"),AllowMultiple=false,FileTypeFilter=new[]{new FilePickerFileType("Images"){Patterns=new[]{"*.png","*.jpg","*.jpeg","*.webp"}}}});
        var path=files.FirstOrDefault()?.TryGetLocalPath();if(path is null)return;
        ChooseUserAvatarBtn.IsEnabled=ChooseAiAvatarBtn.IsEnabled=ResetAvatarsBtn.IsEnabled=false;
        try{await Task.Run(()=>AvatarStore.Shared.Save(user,path));if(!_geminiSettingsClosed)AvatarNotice.Text=LocalizationManager.Text("頭像已儲存，對話立即套用。","Avatar saved and applied to chat.");}
        catch(Exception ex){if(!_geminiSettingsClosed)AvatarNotice.Text=ex.Message;}
        finally{if(!_geminiSettingsClosed)ChooseUserAvatarBtn.IsEnabled=ChooseAiAvatarBtn.IsEnabled=ResetAvatarsBtn.IsEnabled=true;}
    }

    private async Task RefreshGeminiUsageAsync()
    {
        if (_savingGeminiKey || _geminiSettingsClosed) return;
        RefreshGeminiUsageBtn.IsEnabled = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var status = await _assistantClient.UsageAsync(timeout.Token);
            if (!_geminiSettingsClosed) GeminiKeyStatusText.Text = status;
        }
        catch { if (!_geminiSettingsClosed) GeminiKeyStatusText.Text = "靈動島 AI 服務尚未就緒；可在此輸入金鑰並套用。"; }
        finally { if (!_geminiSettingsClosed) RefreshGeminiUsageBtn.IsEnabled = true; }
    }

    private async Task SaveGeminiKeyAsync()
    {
        if (_savingGeminiKey || string.IsNullOrWhiteSpace(GeminiKeyInput.Text)) return;
        if (GeminiFreeConfirmation.IsChecked != true)
        {
            GeminiKeyStatusText.Text = LocalizationManager.Text("請先確認此金鑰的專案使用免費方案，且未啟用付費。", "Confirm that this key's project is on the free tier with billing disabled.");
            return;
        }
        var key = GeminiKeyInput.Text;
        _savingGeminiKey = true; GeminiKeyInput.Text = "";
        GeminiKeyInput.IsEnabled = false; GeminiFreeConfirmation.IsEnabled = false; SaveGeminiKeyBtn.IsEnabled = false; RefreshGeminiUsageBtn.IsEnabled = false;
        GeminiKeyStatusText.Text = LocalizationManager.TranslateLiteral("正在加密儲存並套用…");
        _geminiKeyRequest?.Dispose(); _geminiKeyRequest = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await new GeminiCredentialClient().ConfigureFreeAsync(key, true, _geminiKeyRequest.Token);
            if (!_geminiSettingsClosed) GeminiKeyStatusText.Text = LocalizationManager.TranslateLiteral("金鑰已加密儲存並套用；沒有發出 AI 請求。");
        }
        catch (OperationCanceledException) { if (!_geminiSettingsClosed) GeminiKeyStatusText.Text = LocalizationManager.TranslateLiteral("套用未完成，請稍後再試；每日額度保留。"); }
        catch (Exception ex) { if (!_geminiSettingsClosed) GeminiKeyStatusText.Text = ex.Message; }
        finally
        {
            key = null; _savingGeminiKey = false;
            if (!_geminiSettingsClosed) { GeminiKeyInput.IsEnabled = true; GeminiFreeConfirmation.IsEnabled = true; RefreshGeminiUsageBtn.IsEnabled = true; }
        }
    }

    private static string GetCurrentProcessArchitectureText()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "ARM64",
            Architecture.Arm => "ARM",
            _ => RuntimeInformation.ProcessArchitecture.ToString()
        };
    }

    private void RebuildLocalizedChoiceItems()
    {
        int layerIndex = PersistentLayerCombo.SelectedIndex;
        int modeIndex = PositionModeCombo.SelectedIndex;
        int positionIndex = PositionCombo.SelectedIndex;

        PersistentLayerCombo.Items.Clear();
        PersistentLayerCombo.Items.Add(LocalizationManager.Text("始終置頂", "Always on Top"));
        PersistentLayerCombo.Items.Add(LocalizationManager.Text("桌面層（不置頂）", "Desktop Layer"));

        PositionModeCombo.Items.Clear();
        PositionModeCombo.Items.Add(LocalizationManager.Text("預設位置 + 微調", "Preset + Offset"));
        PositionModeCombo.Items.Add(LocalizationManager.Text("自訂座標", "Custom Coordinates"));

        PositionCombo.Items.Clear();
        string[] zh = ["左上", "頂部居中", "右上", "左側居中", "螢幕居中", "右側居中", "左下", "底部居中", "右下"];
        string[] en = ["Top Left", "Top Center", "Top Right", "Center Left", "Center", "Center Right", "Bottom Left", "Bottom Center", "Bottom Right"];
        for (int i = 0; i < zh.Length; i++)
            PositionCombo.Items.Add(LocalizationManager.Text(zh[i], en[i]));

        if (layerIndex >= 0) PersistentLayerCombo.SelectedIndex = Math.Min(layerIndex, PersistentLayerCombo.Items.Count - 1);
        if (modeIndex >= 0) PositionModeCombo.SelectedIndex = Math.Min(modeIndex, PositionModeCombo.Items.Count - 1);
        if (positionIndex >= 0) PositionCombo.SelectedIndex = Math.Min(positionIndex, PositionCombo.Items.Count - 1);
    }

    private void InitializeWindowChrome()
    {
        MinimizeSettingsBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeSettingsBtn.Click += (_, _) => ToggleSettingsMaximize();
        CloseSettingsBtn.Click += (_, _) => Close();
        SettingsTitleDragArea.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) ToggleSettingsMaximize();
            else BeginMoveDrag(e);
            e.Handled = true;
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty) UpdateWindowChrome();
        };
        UpdateWindowChrome();
    }

    private void ToggleSettingsMaximize()
    {
        if (CanResize) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void UpdateWindowChrome()
    {
        bool maximized = WindowState == WindowState.Maximized;
        void Caption(Button button, string text)
        {
            AutomationProperties.SetName(button, text);
            ToolTip.SetTip(button, text);
        }
        Caption(MinimizeSettingsBtn, LocalizationManager.Text("最小化", "Minimize"));
        Caption(MaximizeSettingsBtn, maximized ? LocalizationManager.Text("還原視窗", "Restore window") : LocalizationManager.Text("最大化", "Maximize"));
        Caption(CloseSettingsBtn, LocalizationManager.Text("關閉設定", "Close settings"));
        MaximizeSettingsIcon.Data = Geometry.Parse(maximized
            ? "M 3,0 L 12,0 L 12,9 L 10,9 L 10,2 L 3,2 Z M 0,3 L 9,3 L 9,12 L 0,12 Z M 1,4 L 1,11 L 8,11 L 8,4 Z"
            : "M 0,0 L 12,0 L 12,12 L 0,12 Z M 1,1 L 1,11 L 11,11 L 11,1 Z");
        SettingsSectionTitle.Text = (ModuleTabs.SelectedItem as TabItem)?.Header as string
            ?? LocalizationManager.Text("設定", "Settings");
        string[] sectionLabels={"ASSISTANT","NOTIFICATIONS","AUDIO","DISPLAY","HUD EDITOR","ABOUT"};
        int sectionIndex=0;
        foreach(var tab in ModuleTabs.Items.OfType<TabItem>())
        {
            var section=new SettingsSection($"{sectionIndex+1:00}",sectionLabels[sectionIndex++]);
            if(tab.Tag is not SettingsSection previous || previous!=section)tab.Tag=section;
        }
    }

    public sealed record SettingsSection(string Number,string Label);

    private async void OnLanguageSelected(AppLanguage language)
    {
        string preference = LocalizationManager.PreferenceFor(language);
        bool languageChanged = LocalizationManager.Current != language;

        _settings = _settings with { UiLanguage = preference };
        LocalizationManager.SetLanguage(language);

        ApplyLocalization();
        UpdateLanguageSwitchVisual();

        // Language is an immediate UI preference. Persist only the language choice here;
        // other editor changes still require Save & Apply.
        _saveSettings(_settings);

        // Persistent and transient HUD renderers read LocalizationManager at render time,
        // so their next live refresh switches language without replacing unsaved settings.
        if (languageChanged)
            await Task.Delay(1);
    }

    private void ApplyLocalization()
    {
        EndfieldBrandMark.Data=(Geometry)this.FindResource("Geo.Endfield.Industries")!;
        LocalizationManager.ApplyStaticText(this);
        UpdateWindowChrome();
        _ = RefreshGeminiUsageAsync();RefreshNotificationStatus();
        RebuildLocalizedChoiceItems();
        if (_monitorComboReady)
            PopulateMonitorCombo(force: true);

        Customizer.ApplyLocalization();

        string currentVersion = GetCurrentVersionText();
        AboutVersionText.Text = $"v{currentVersion}";
        UpdateCurrentVersionText.Text = LocalizationManager.Text($"目前版本：v{currentVersion}", $"Current: v{currentVersion}");
        if (LastUpdateResult is { } result)
            ApplyUpdateCheckResult(result);
        else
        {
            UpdateLatestVersionText.Text = LocalizationManager.Text("最新版本：尚未取得", "Latest: not checked");
            UpdateStatusText.Text = LocalizationManager.Text("狀態：尚未檢查", "Status: not checked");
        }

        RefreshMaintenanceStatusLocalization();
    }

    private void SetMaintenanceStatus(string zh, string en)
    {
        _maintenanceStatusZh = zh;
        _maintenanceStatusEn = en;
        RefreshMaintenanceStatusLocalization();
    }

    private void RefreshMaintenanceStatusLocalization()
    {
        if (_maintenanceStatusZh is null || _maintenanceStatusEn is null)
            return;

        MaintenanceStatusText.Text = LocalizationManager.Text(_maintenanceStatusZh, _maintenanceStatusEn);
    }

    private void UpdateLanguageSwitchVisual()
    {
        var activeBg = new SolidColorBrush(Color.Parse("#C6CA4C"));
        var inactiveBg = new SolidColorBrush(Color.Parse("#2B2B2E"));
        var activeFg = new SolidColorBrush(Color.Parse("#171719"));
        var inactiveFg = new SolidColorBrush(Color.Parse("#F0F0F2"));

        bool english = LocalizationManager.IsEnglish;
        LanguageChineseBtn.Classes.Set("industrialPrimary", !english);
        LanguageEnglishBtn.Classes.Set("industrialPrimary", english);
        LanguageChineseBtn.Background = english ? inactiveBg : activeBg;
        LanguageChineseBtn.Foreground = english ? inactiveFg : activeFg;
        LanguageChineseBtn.FontWeight = english ? FontWeight.Normal : FontWeight.SemiBold;

        LanguageEnglishBtn.Background = english ? activeBg : inactiveBg;
        LanguageEnglishBtn.Foreground = english ? activeFg : inactiveFg;
        LanguageEnglishBtn.FontWeight = english ? FontWeight.SemiBold : FontWeight.Normal;
    }

    private void PopulateMonitorCombo(bool force = false)
    {
        if (_monitorComboReady && !force) return;
        _monitorComboReady = true;

        MonitorCombo.Items.Clear();
        MonitorCombo.Items.Add(LocalizationManager.Text("主螢幕（自動）", "Primary Display (Auto)"));

        var screens = Screens.All;
        for (int i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            var area = s.WorkingArea;
            string primary = ReferenceEquals(s, Screens.Primary) ? LocalizationManager.Text(" · 主", " · Primary") : "";
            MonitorCombo.Items.Add(LocalizationManager.Text($"螢幕 {i + 1} · {area.Width}×{area.Height}{primary}", $"Display {i + 1} · {area.Width}×{area.Height}{primary}"));
        }

        int wanted = _settings.MonitorIndex < 0 ? 0 : _settings.MonitorIndex + 1;
        MonitorCombo.SelectedIndex = wanted >= 0 && wanted < MonitorCombo.Items.Count ? wanted : 0;
    }

    private void UpdateModeUi()
    {
        bool persistent = AlwaysVisibleSwitch.IsChecked == true;
        PersistentLayerCombo.IsEnabled = persistent;
        PersistentLayerCombo.Opacity = persistent ? 1.0 : 0.45;

        bool showClock = ShowClockSwitch.IsChecked == true;
        Use24HourSwitch.IsEnabled = showClock;
        Use24HourSwitch.Opacity = showClock ? 1.0 : 0.45;
        ShowDateSwitch.IsEnabled = showClock;
        ShowDateSwitch.Opacity = showClock ? 1.0 : 0.45;

        bool custom = PositionModeCombo.SelectedIndex == 1;
        PresetPositionPanel.IsEnabled = !custom;
        PresetPositionPanel.Opacity = custom ? 0.40 : 1.0;
        CustomPositionPanel.IsEnabled = custom;
        CustomPositionPanel.Opacity = custom ? 1.0 : 0.40;
    }

    private void ApplySettingsToControls(AppSettings settings)
    {
        _settings = settings;

        ScaleBox.Value = (decimal)settings.GlobalScale;
        DurationBox.Value = (decimal)settings.DisplayDurationSeconds;
        BounceBox.Value = (decimal)settings.BounceStrength;
        RippleIntensityBox.Value = (decimal)settings.RippleIntensity;
        RippleSpreadBox.Value = (decimal)settings.RippleSpread;
        HudOpacitySlider.Value = Math.Clamp(settings.HudOpacity * 100.0, 10.0, 100.0);
        UpdateOpacityLabel();

        HudEnabledSwitch.IsChecked = settings.HudEnabled;
        StartupSwitch.IsChecked = settings.StartWithWindows;
        AlwaysVisibleSwitch.IsChecked = settings.AlwaysVisible;
        ShowClockSwitch.IsChecked = settings.ShowClock;
        Use24HourSwitch.IsChecked = settings.Use24HourClock;
        ShowDateSwitch.IsChecked = settings.ShowDate;
        NotificationsEnabledSwitch.IsChecked = settings.NotificationsEnabled;
        NotificationHideContentSwitch.IsChecked = settings.NotificationHideContent;
        PersistentLayerCombo.SelectedIndex = settings.PersistentLayer == PersistentHudLayer.Desktop ? 1 : 0;
        PositionModeCombo.SelectedIndex = settings.PositionMode == HudPositionMode.CustomCoordinates ? 1 : 0;
        PositionCombo.SelectedIndex = PositionToIndex(settings.HudPosition);
        OffsetXBox.Value = settings.HudOffsetX;
        OffsetYBox.Value = settings.HudOffsetY;
        CustomXBox.Value = settings.HudCustomX;
        CustomYBox.Value = settings.HudCustomY;

        if (_monitorComboReady)
        {
            int wanted = settings.MonitorIndex < 0 ? 0 : settings.MonitorIndex + 1;
            MonitorCombo.SelectedIndex = wanted >= 0 && wanted < MonitorCombo.Items.Count ? wanted : 0;
        }

        Customizer.Load(settings.CustomHud, _hud);
        UpdateModeUi();
    }

    private AppSettings CollectSettingsFromUi()
        => _settings with
        {
            HudEnabled = HudEnabledSwitch.IsChecked == true,
            StartWithWindows = StartupSwitch.IsChecked == true,
            UiLanguage = _settings.UiLanguage,
            GlobalScale = (double)(ScaleBox.Value ?? (decimal)AppSettings.DefaultGlobalScale),
            DisplayDurationSeconds = (double)(DurationBox.Value ?? (decimal)AppSettings.DefaultDisplayDurationSeconds),
            BounceStrength = (double)(BounceBox.Value ?? (decimal)AppSettings.DefaultBounceStrength),
            RippleIntensity = (double)(RippleIntensityBox.Value ?? (decimal)AppSettings.DefaultRippleIntensity),
            RippleSpread = (double)(RippleSpreadBox.Value ?? (decimal)AppSettings.DefaultRippleSpread),
            HudOpacity = Math.Clamp(HudOpacitySlider.Value / 100.0, 0.10, 1.0),
            ShowClock = ShowClockSwitch.IsChecked == true,
            Use24HourClock = Use24HourSwitch.IsChecked == true,
            ShowDate = ShowDateSwitch.IsChecked == true,
            NotificationsEnabled = NotificationsEnabledSwitch.IsChecked == true,
            NotificationHideContent = NotificationHideContentSwitch.IsChecked == true,

            AlwaysVisible = AlwaysVisibleSwitch.IsChecked == true,
            PersistentLayer = PersistentLayerCombo.SelectedIndex == 1
                ? PersistentHudLayer.Desktop
                : PersistentHudLayer.Topmost,

            PositionMode = PositionModeCombo.SelectedIndex == 1
                ? HudPositionMode.CustomCoordinates
                : HudPositionMode.Preset,
            HudPosition = IndexToPosition(PositionCombo.SelectedIndex),
            HudOffsetX = (int)(OffsetXBox.Value ?? 0),
            HudOffsetY = (int)(OffsetYBox.Value ?? 0),
            HudCustomX = (int)(CustomXBox.Value ?? 0),
            HudCustomY = (int)(CustomYBox.Value ?? 0),
            MonitorIndex = MonitorCombo.SelectedIndex <= 0 ? -1 : MonitorCombo.SelectedIndex - 1,

            CustomHud = Customizer.ExportSettings(),
        };

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        SaveBtn.IsEnabled = false;
        SaveBtn.Content = LocalizationManager.Text("應用中…", "Applying…");
        try
        {
            var candidate = CollectSettingsFromUi();
            _saveSettings(candidate);
            _settings = candidate;
            _applyStartup(_settings.StartWithWindows);
            await _runtime.ApplySettingsWithTransitionAsync(_settings);
            SettingsApplied?.Invoke(_settings);
        }
        catch (Exception ex)
        {
            AppLog.Error("Unable to save/apply settings; application remains running.", ex);
            SaveBtn.Content = LocalizationManager.Text("儲存失敗", "Save failed");
            SetMaintenanceStatus("設定未成功套用，請查看記錄；程式仍可繼續使用。",
                "Settings could not be applied. Check logs; the application remains available.");
            return;
        }
        finally
        {
            SaveBtn.IsEnabled = true;
        }

        SaveBtn.Content = LocalizationManager.Text("已儲存", "Saved");
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        timer.Tick += (_, _) =>
        {
            SaveBtn.Content = LocalizationManager.Text("儲存並套用", "Save & Apply");
            timer.Stop();
        };
        timer.Start();
    }

    private async void OnExportSettings(object? sender, RoutedEventArgs e)
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = LocalizationManager.Text("匯出 終末地 靈動島 設定", "Export Endfield Dynamic Island Config"),
                SuggestedFileName = $"EndfieldChargePlus-settings-{DateTime.Now:yyyyMMdd-HHmmss}.json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType(LocalizationManager.Text("JSON 設定", "JSON Config")) { Patterns = new[] { "*.json" } },
                },
            });

            if (file is null) return;
            SettingsManager.ExportToFile(file.Path.LocalPath, CollectSettingsFromUi());
            SetMaintenanceStatus("設定已匯出。", "Config exported.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to export settings from the settings UI.", ex);
            SetMaintenanceStatus($"匯出失敗：{ex.Message}", "Export failed. See logs.");
        }
    }

    private async void OnImportSettings(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LocalizationManager.Text("匯入 終末地 靈動島 設定", "Import Endfield Dynamic Island Config"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(LocalizationManager.Text("JSON 設定", "JSON Config")) { Patterns = new[] { "*.json" } },
                },
            });

            if (files.Count == 0) return;

            string? backup = SettingsManager.BackupCurrent("before-import");
            AppSettings imported = SettingsManager.ImportFromFile(files[0].Path.LocalPath);
            _saveSettings(imported);
            _applyStartup(imported.StartWithWindows);
            LocalizationManager.Initialize(imported.UiLanguage);
            ApplyLocalization();
            UpdateLanguageSwitchVisual();
            ApplySettingsToControls(imported);
            await _runtime.ApplySettingsWithTransitionAsync(imported);
            SettingsApplied?.Invoke(imported);

            if (backup is null)
                SetMaintenanceStatus("設定已匯入並應用。", "Config imported and applied.");
            else
                SetMaintenanceStatus(
                    $"設定已匯入並應用；舊設定已備份到 {Path.GetFileName(backup)}。",
                    $"Config imported and applied; previous config backed up as {Path.GetFileName(backup)}.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to import settings from the settings UI.", ex);
            SetMaintenanceStatus($"匯入失敗：{ex.Message}", "Import failed. See logs.");
        }
    }

    private void OnBackupSettings(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Save the current editor state first so the manual backup matches what the user sees.
            var current = CollectSettingsFromUi();
            _saveSettings(current);
            string? backup = SettingsManager.BackupCurrent("manual");
            if (backup is null)
                SetMaintenanceStatus("目前沒有可備份的設定檔案。", "No config file is available to back up.");
            else
                SetMaintenanceStatus(
                    $"設定已備份：{Path.GetFileName(backup)}",
                    $"Config backed up: {Path.GetFileName(backup)}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to create a manual settings backup.", ex);
            SetMaintenanceStatus($"備份失敗：{ex.Message}", "Backup failed. See logs.");
        }
    }

    private void OnOpenLogsFolder(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppLog.LogsDirectory,
                UseShellExecute = true,
            });
            SetMaintenanceStatus("已開啟紀錄資料夾。", "Logs folder opened.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to open the logs directory.", ex);
            SetMaintenanceStatus($"無法開啟紀錄資料夾：{ex.Message}", "Could not open the logs folder. See logs.");
        }
    }

    private void OnResetAllSettings(object? sender, RoutedEventArgs e)
    {
        var defaults = SettingsManager.CreateDefaults();
        LocalizationManager.Initialize(defaults.UiLanguage);
        ApplyLocalization();
        UpdateLanguageSwitchVisual();
        ApplySettingsToControls(defaults);
        SetMaintenanceStatus("已載入首次安裝預設值；點選右上角“儲存並套用”後生效。", "First-run defaults loaded; click Save & Apply to commit them.");
        AppLog.Info("Default settings were loaded into the settings editor (not yet saved). ");
    }

    private void OnResetSizeAnimation(object? sender, RoutedEventArgs e)
    {
        ScaleBox.Value = (decimal)AppSettings.DefaultGlobalScale;
        DurationBox.Value = (decimal)AppSettings.DefaultDisplayDurationSeconds;
        BounceBox.Value = (decimal)AppSettings.DefaultBounceStrength;
        RippleIntensityBox.Value = (decimal)AppSettings.DefaultRippleIntensity;
        RippleSpreadBox.Value = (decimal)AppSettings.DefaultRippleSpread;
    }

    private void UpdateOpacityLabel()
    {
        HudOpacityValueText.Text = $"{Math.Round(HudOpacitySlider.Value):0}%";
    }

    private static HttpClient CreateUpdateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EndfieldChargePlus", GetCurrentVersionText()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static string GetCurrentVersionText()
        => typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    private async void OnCheckUpdate(object? sender, RoutedEventArgs e)
    {
        CheckUpdateBtn.IsEnabled = false;
        CheckUpdateBtn.Content = LocalizationManager.Text("檢查中…", "Checking…");
        UpdateStatusText.Text = LocalizationManager.Text("狀態：正在連線 GitHub 檢查更新…", "Status: checking GitHub…");

        UpdateCheckResult result = await CheckForUpdatesAsync();
        ApplyUpdateCheckResult(result);

        CheckUpdateBtn.Content = LocalizationManager.Text("檢查更新", "Check Updates");
        CheckUpdateBtn.IsEnabled = true;
    }

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync()
    {
        string currentText = GetCurrentVersionText();

        try
        {
            string? latestTag = await TryGetLatestReleaseTagAsync();
            if (string.IsNullOrWhiteSpace(latestTag))
                latestTag = await TryGetLatestTagAsync();

            if (string.IsNullOrWhiteSpace(latestTag))
            {
                return StoreUpdateResult(new UpdateCheckResult(
                    currentText,
                    LatestVersion: null,
                    Status: UpdateStatusKind.NoRemoteVersion,
                    HasUpdate: false,
                    CheckSucceeded: true));
            }

            string normalizedLatest = NormalizeVersionTag(latestTag);
            Version? current = TryParseVersion(currentText);
            Version? latest = TryParseVersion(normalizedLatest);

            UpdateStatusKind status;
            bool hasUpdate = false;
            if (current is null || latest is null)
            {
                status = UpdateStatusKind.Uncomparable;
            }
            else if (latest > current)
            {
                status = UpdateStatusKind.UpdateAvailable;
                hasUpdate = true;
            }
            else if (latest == current)
            {
                status = UpdateStatusKind.UpToDate;
            }
            else
            {
                status = UpdateStatusKind.LocalNewer;
            }

            return StoreUpdateResult(new UpdateCheckResult(
                currentText,
                LatestVersion: normalizedLatest,
                Status: status,
                HasUpdate: hasUpdate,
                CheckSucceeded: true));
        }
        catch (HttpRequestException ex)
        {
            AppLog.Error("Update check HTTP request failed.", ex);
            return StoreUpdateResult(new UpdateCheckResult(
                currentText,
                LatestVersion: null,
                Status: UpdateStatusKind.NetworkError,
                HasUpdate: false,
                CheckSucceeded: false,
                Detail: ex.StatusCode?.ToString()));
        }
        catch (TaskCanceledException ex)
        {
            AppLog.Error("Update check timed out.", ex);
            return StoreUpdateResult(new UpdateCheckResult(
                currentText,
                LatestVersion: null,
                Status: UpdateStatusKind.Timeout,
                HasUpdate: false,
                CheckSucceeded: false));
        }
        catch (Exception ex)
        {
            AppLog.Error("Update check failed.", ex);
            return StoreUpdateResult(new UpdateCheckResult(
                currentText,
                LatestVersion: null,
                Status: UpdateStatusKind.Error,
                HasUpdate: false,
                CheckSucceeded: false));
        }
    }

    private static UpdateCheckResult StoreUpdateResult(UpdateCheckResult result)
    {
        _lastUpdateResult = result;
        return result;
    }

    public void ApplyUpdateCheckResult(UpdateCheckResult result)
    {
        UpdateCurrentVersionText.Text = LocalizationManager.Text($"目前版本：v{result.CurrentVersion}", $"Current: v{result.CurrentVersion}");
        UpdateLatestVersionText.Text = result.LatestVersionText;
        UpdateStatusText.Text = result.StatusText;
    }

    private static async Task<string?> TryGetLatestReleaseTagAsync()
    {
        using HttpResponseMessage response = await UpdateHttpClient.GetAsync(
            "https://api.github.com/repos/OverGreen996/Endfield-Dynamic-Island/releases/latest");

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        using JsonDocument json = await JsonDocument.ParseAsync(stream);
        return json.RootElement.TryGetProperty("tag_name", out JsonElement tag)
            ? tag.GetString()
            : null;
    }

    private static async Task<string?> TryGetLatestTagAsync()
    {
        using HttpResponseMessage response = await UpdateHttpClient.GetAsync(
            "https://api.github.com/repos/OverGreen996/Endfield-Dynamic-Island/tags?per_page=1");

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        using JsonDocument json = await JsonDocument.ParseAsync(stream);
        JsonElement root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            return null;

        JsonElement first = root[0];
        return first.TryGetProperty("name", out JsonElement name)
            ? name.GetString()
            : null;
    }

    private static string NormalizeVersionTag(string tag)
    {
        string value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];

        int suffix = value.IndexOfAny(['-', '+']);
        if (suffix >= 0)
            value = value[..suffix];

        return value;
    }

    private static Version? TryParseVersion(string value)
        => Version.TryParse(NormalizeVersionTag(value), out Version? version) ? version : null;

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch { }
    }

    private void OnOpenSettingsFolder(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsManager.SettingsDirectory);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SettingsManager.SettingsDirectory,
                UseShellExecute = true,
            });
        }
        catch { }
    }

    private static int PositionToIndex(HudPosition p) => p switch
    {
        HudPosition.TopLeft => 0,
        HudPosition.TopCenter => 1,
        HudPosition.TopRight => 2,
        HudPosition.CenterLeft => 3,
        HudPosition.Center => 4,
        HudPosition.CenterRight => 5,
        HudPosition.BottomLeft => 6,
        HudPosition.BottomCenter => 7,
        HudPosition.BottomRight => 8,
        _ => 1,
    };

    private static HudPosition IndexToPosition(int index) => index switch
    {
        0 => HudPosition.TopLeft,
        1 => HudPosition.TopCenter,
        2 => HudPosition.TopRight,
        3 => HudPosition.CenterLeft,
        4 => HudPosition.Center,
        5 => HudPosition.CenterRight,
        6 => HudPosition.BottomLeft,
        7 => HudPosition.BottomCenter,
        8 => HudPosition.BottomRight,
        _ => HudPosition.TopCenter,
    };
}
