using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using EndfieldChargePlus.Assistant;
using EndfieldChargePlus.Diagnostics;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Views;

public partial class AssistantIslandWindow : Window
{
    private readonly AssistantHubClient _client = new();
    private readonly XngPluginClient _xng = new();
    private readonly AssistantSession _session;
    private readonly PersonalAssistantStore _personal;
    private WindowsHudHitTest? _hitTest;
    private readonly PaintedIslandRegion _paintedRegion = new();
    private CancellationTokenSource? _request, _usageRequest, _xngRequest;
    private AppSettings _settings = new();
    private bool _busy, _layoutPending, _disposed;
    private int _generation, _visibleTurns = 20;
    private MemoryPalaceWindow? _memoryPalace;
    private Task? _hideAnimation;
    private bool _closing;
    private int _hideGeneration;
    private readonly IBrush? _normalBackground, _normalOutline;
    private PastedImage? _pastedImage;
    private Avalonia.Media.Imaging.Bitmap? _previewBitmap;
    private bool _readingImage;
    public event Action? IslandHidden;
    public event Action? MusicRequested;
    public event Action? MemoryRequested;

    public AssistantIslandWindow(PersonalAssistantStore? personal=null,AssistantSession? session=null)
    {
        _personal=personal??PersonalAssistantStore.Shared;_session=session??new AssistantSession();
        InitializeComponent();
        LocalizationManager.LanguageChanged+=ApplyLanguage;
        AvatarStore.Shared.Changed+=OnAvatarChanged;
        _normalBackground=Island.Background;_normalOutline=Island.BorderBrush;
        InputBox.TextChanged += (_, _) => QueueLayout();
        LayoutUpdated += (_, _) => UpdateInputRegion();
        InputBox.AddHandler(KeyDownEvent, OnInputKey, RoutingStrategies.Tunnel);
        RemoveImageButton.Click += (_,_) => ClearPastedImage();
        SendButton.Click += async (_, _) => { if (_busy) _request?.Cancel(); else await SendAsync(); };
        BodyHeader.PointerReleased += async (_, e) =>
        { if (e.InitialPressMouseButton == MouseButton.Left) { e.Handled = true; await HideAnimatedAsync(); } };
        NewConversationItem.Click += (_, _) => NewConversation();
        MemoryPalaceItem.Click += (_, _) =>
        {
            if(MemoryRequested is not null){MemoryRequested.Invoke();return;}
            if (_memoryPalace is null) { _memoryPalace=new MemoryPalaceWindow(_personal);_memoryPalace.Closed+=(_,_)=>_memoryPalace=null; }
            _memoryPalace.Show();_memoryPalace.Activate();
        };
        UsageItem.Click += async (_, _) => await RefreshUsageAsync();
        XngStatusItem.Click += async (_, _) => await RefreshXngAsync();
        XngManagerItem.Click += async (_, _) =>
        {
            try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6)); await _xng.OpenManagerAsync(timeout.Token); }
            catch (Exception ex) { StatusText.Text = ex.Message; QueueLayout(); }
        };
        MusicItem.Click += (_, _) => MusicRequested?.Invoke();
        HideItem.Click += async (_, _) => await HideAnimatedAsync();
        KeyDown += async (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; await HideAnimatedAsync(); } };
        Closed += (_, _) =>
        {
            _disposed = true;LocalizationManager.LanguageChanged-=ApplyLanguage;AvatarStore.Shared.Changed-=OnAvatarChanged;_hideGeneration++;_closing=false;IslandTransition.Cancel(this); _request?.Cancel(); _usageRequest?.Cancel(); _xngRequest?.Cancel();
            _hitTest?.Dispose(); _client.Dispose(); _xng.Dispose();
            _memoryPalace?.Close();
            _previewBitmap?.Dispose();_pastedImage=null;
        };
        ApplyLanguage();
    }
    private void OnAvatarChanged()=>Dispatcher.UIThread.Post(()=>{if(!_disposed){RenderConversation();QueueLayout();}});
    private void ApplyLanguage()
    {
        EndfieldBrandMark.Data=(Geometry)this.FindResource(LocalizationManager.IsEnglish?"Geo.Endfield.Icon.en":"Geo.Endfield.Icon.zh")!;
        Title=LocalizationManager.TranslateLiteral(Title);LocalizationManager.ApplyStaticText(BodyHeader);
        if(Island.ContextMenu is {}menu)LocalizationManager.ApplyStaticText(menu);
        InputBox.Watermark=LocalizationManager.TranslateLiteral(InputBox.Watermark?.ToString());
        int selectedMode=Math.Max(0,ModeCombo.SelectedIndex);
        if(ModeCombo.ItemsSource is null)ModeCombo.Items.Clear();
        ModeCombo.ItemsSource=new[]{"自動","只聊天","只搜尋","搜尋＋AI"}.Select(choice=>new ComboBoxItem{Content=LocalizationManager.TranslateLiteral(choice)}).ToArray();
        ModeCombo.SelectedIndex=selectedMode;
        SendButton.Content=LocalizationManager.Text(_busy?"取消":"送出",_busy?"Cancel":"Send");
        RemoveImageButton.Content=LocalizationManager.Text("移除","Remove");
        UpdateImagePreviewText();
        StatusText.Text=LocalizationManager.TranslateLiteral(StatusText.Text);RenderConversation();QueueLayout();
    }

    public void ShowInput(AppSettings settings)
    {
        if(_closing){_hideGeneration++;_closing=false;_hideAnimation=null;IslandTransition.Cancel(this);ResetCloseAppearance();}
        _settings = settings; ShowActivated = true; RefreshLayout();
        if (!IsVisible) Show();
        PositionIsland(); EnsureHitTest(); Activate();
        Dispatcher.UIThread.Post(() => { PositionIsland(); EnsureHitTest(); InputBox.Focus(); }, DispatcherPriority.Input);
        _ = RefreshUsageAsync();
    }
    public void HideIsland()
    {
        bool visible=IsVisible;_hideGeneration++;_closing=false;_hideAnimation=null;
        IslandTransition.Cancel(this);ResetCloseAppearance();
        if (!visible) return;
        Island.ContextMenu?.Close(); Hide(); Opacity = 1;
        IslandHidden?.Invoke();
    }
    private void ResetCloseAppearance()
    {
        Island.Background=_normalBackground;Island.BorderBrush=_normalOutline;Island.Clip=null;
        AssistantContent.Opacity=1;AssistantContent.RenderTransform=null;
        CloseSurface.IsVisible=false;
        Opacity=1;_hitTest?.SetTransitionInputTransparent(false);
    }
    public Task HideAnimatedAsync()
    {
        if(_disposed||!IsVisible)return Task.CompletedTask;
        if(_closing&&_hideAnimation is not null)return _hideAnimation;
        _closing=true;int generation=++_hideGeneration;Island.ContextMenu?.Close();
        _hitTest?.SetTransitionInputTransparent(true);
        return _hideAnimation=CloseCoreAsync(generation);
    }
    private async Task CloseCoreAsync(int generation)
    {
        try { await IslandTransition.CollapseUpAsync(this,Island,AssistantContent,CloseSurface); }
        catch(Exception ex) { AppLog.Error("Assistant close animation did not complete.",ex); }
        if(!_disposed&&_closing&&generation==_hideGeneration)HideIsland();
    }
    private void NewConversation()
    {
        _generation++; _request?.Cancel(); _busy = false;
        InputBox.IsEnabled = true; SendButton.Content = LocalizationManager.TranslateLiteral("送出");
        _session.Clear(); _visibleTurns = 20; InputBox.Text = "";
        ClearPastedImage();
        StatusText.Text = _session.StorageNotice ?? "新對話 · Alt+A 喚出 · Shift+Enter 換行";
        RenderConversation(); QueueLayout(); InputBox.Focus();
    }
    private async void OnInputKey(object? sender, KeyEventArgs e)
    {
        if(e.Key==Key.V&&e.KeyModifiers.HasFlag(KeyModifiers.Control)&&!_busy&&!_readingImage)
        {
            try
            {
                var encoded=ClipboardImage.ReadEncodedImage();
                if(encoded is not null)
                {
                    e.Handled=true;_readingImage=true;int generation=_generation;
                    var image=await Task.Run(()=>ClipboardImage.Normalize(encoded));
                    if(!_disposed&&generation==_generation)SetPastedImage(image);
                    return;
                }
            }
            catch(Exception ex){e.Handled=true;StatusText.Text=ex is InvalidOperationException?ex.Message:LocalizationManager.Text("圖片無法貼上，請重新複製。","Unable to paste image. Copy it again.");}
            finally{_readingImage=false;}
        }
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        if (InputBox.GetVisualDescendants().OfType<TextPresenter>().Any(p => !string.IsNullOrEmpty(p.PreeditText))) return;
        e.Handled = true; if (!_busy) await SendAsync();
    }
    public void SetPastedImage(PastedImage image)
    {
        var bitmap = new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(Convert.FromBase64String(image.data)));
        _previewBitmap?.Dispose();_previewBitmap=bitmap;_pastedImage=image;
        PastedImagePreview.Source=bitmap;ImagePreviewFrame.IsVisible=true;
        UpdateImagePreviewText();QueueLayout();
    }
    private void UpdateImagePreviewText()
    {
        ImagePreviewCaption.Text=_pastedImage is null?"":LocalizationManager.Text(
            $"已貼上圖片 · {_pastedImage.Width} × {_pastedImage.Height}",
            $"Pasted image · {_pastedImage.Width} × {_pastedImage.Height}");
        ImagePreviewNotice.Text=LocalizationManager.Text("按送出才判讀 · 圖片不存入記憶","Analyzed only after Send · No image memory");
    }
    private void ClearPastedImage()
    {
        _pastedImage=null;PastedImagePreview.Source=null;_previewBitmap?.Dispose();_previewBitmap=null;
        ImagePreviewFrame.IsVisible=false;QueueLayout();
    }
    private async Task SendAsync()
    {
        var question = InputBox.Text?.Trim()??"";
        if (_busy || _closing || _readingImage || (string.IsNullOrWhiteSpace(question)&&_pastedImage is null)) return;
        _request?.Dispose(); _request = new CancellationTokenSource();
        var token = _request.Token; var generation = _generation;
        _busy = true; InputBox.IsEnabled = false; SendButton.Content = LocalizationManager.TranslateLiteral("取消");
        var mode = ModeCombo.SelectedIndex switch { 1 => "chat", 2 => "web", 3 => "search", _ => "auto" };
        StatusText.Text = mode == "chat" ? LocalizationManager.TranslateLiteral("Gemini 3.5 Lite 思考中…") : LocalizationManager.TranslateLiteral("取得共用搜尋與 AI 回覆…");
        try
        {
            var personal=_personal;
            var attached=_pastedImage;
            var local=attached is null?personal.Handle(question,DateTimeOffset.Now):null;
            if(local is null&&attached is null)
            {
                var memory=personal.ObserveSelfStatement(question,DateTimeOffset.Now);
                if(memory is not null)local=new LocalAssistantResult($"已記住〔{memory.Category}〕{memory.Text}\n可以在右鍵 → 記憶宮殿修改或刪除。",memory.Id);
            }
            var reply = local is not null ? new AssistantReply(local.Text,"local",null,false,null,null,null)
                : await _client.AskAsync(question, personal.WithMemory(_session.ModelHistory(),question), mode, token,attached?.ToInput());
            if (generation != _generation || _disposed) return;
            if (attached is null && reply.answer_kind == "model" && reply.memory_suggestions is { Length: > 0 })
            {
                try
                {
                    var saved = personal.AcceptModelSuggestion(question, reply.memory_suggestions[0], DateTimeOffset.Now);
                    if (saved is not null) reply = reply with { text = reply.text + LocalizationManager.Text(
                        $"\n\n已記住〔{saved.Category}〕{saved.Text}。可在記憶宮殿修改或刪除。",
                        $"\n\nSaved to memory: {saved.Text}. You can edit or delete it in Memory Palace.") };
                }
                catch (InvalidOperationException ex)
                {
                    // Preserve the successful answer when the separate local save is blocked or fails.
                    reply = reply with { text = reply.text + "\n\n" + ex.Message };
                }
                reply = reply with { memory_suggestions = null };
            }
            _session.Append(attached is null?question:(string.IsNullOrWhiteSpace(question)?LocalizationManager.Text("〔已貼上圖片〕","[Pasted image]"):question+LocalizationManager.Text("\n〔已附圖片〕","\n[Image attached]")), reply);
            InputBox.Text = ""; ClearPastedImage(); RenderConversation();
            StatusText.Text = _session.StorageNotice ?? (reply.context?.reduced == true
                ? $"已保留 {_session.Turns.Count} 輪 · 本次使用最近內容及相關舊對話節錄"
                : reply.answer_kind == "local" ? LocalizationManager.TranslateLiteral("本機提醒／記憶 · 未使用 Gemini 額度")
                : reply.answer_kind == "model" ? LocalizationManager.TranslateLiteral("Gemini 3.5 Lite · 對話已保留") : LocalizationManager.TranslateLiteral("XNG 免費搜尋證據 · 未產生 Gemini 回答"));
            if (_session.OlderTurnsRemoved) StatusText.Text += LocalizationManager.TranslateLiteral(" · 最舊內容已超過本機儲存上限");
        }
        catch (OperationCanceledException)
        { if (generation == _generation && !_disposed) StatusText.Text = LocalizationManager.TranslateLiteral("本次已取消；已送達 Gemini 的請求仍可能計入額度。"); }
        catch (Exception ex)
        {
            if (generation != _generation || _disposed) return;
            StatusText.Text = ex is InvalidOperationException ? ex.Message : LocalizationManager.TranslateLiteral("無法連線至共用 AI 服務。請先啟動 Gemini Hub（8890）。");
            if (ex is not InvalidOperationException) AppLog.Error("Assistant request did not complete.", ex);
        }
        finally
        {
            if (generation == _generation && !_disposed)
            {
                _busy = false; InputBox.IsEnabled = true; SendButton.Content = LocalizationManager.TranslateLiteral("送出");
                QueueLayout(); if (IsVisible) InputBox.Focus();
            }
        }
    }
    private async Task RefreshUsageAsync()
    {
        _usageRequest?.Cancel(); _usageRequest?.Dispose();
        _usageRequest = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            var text = await _client.UsageAsync(_usageRequest.Token);
            if (!_disposed && !_busy) { StatusText.Text = _session.StorageNotice ?? text; QueueLayout(); }
        }
        catch { if (!_disposed && !_busy) StatusText.Text = LocalizationManager.TranslateLiteral("AI 共用服務尚未連線 · Alt+A 喚出 · 右鍵新對話"); }
    }
    private async Task RefreshXngAsync()
    {
        _xngRequest?.Cancel(); _xngRequest?.Dispose();
        _xngRequest = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        if (!_busy) StatusText.Text = LocalizationManager.TranslateLiteral("正在查詢 XNG 插件版本…");
        try
        {
            var status = await _xng.StatusAsync(_xngRequest.Token);
            if (!_disposed && !_busy) { StatusText.Text = status; QueueLayout(); }
        }
        catch { if (!_disposed && !_busy) { StatusText.Text = LocalizationManager.TranslateLiteral("無法查詢 XNG；請確認共用 AI 服務已啟動。"); QueueLayout(); } }
    }
    private void RenderConversation()
    {
        ConversationPanel.Children.Clear();
        if (_session.Turns.Count > _visibleTurns)
        {
            var earlier = new Button { Content = LocalizationManager.Text($"顯示較早的對話（共 {_session.Turns.Count} 輪）",$"Show earlier messages ({_session.Turns.Count} turns)"), FontSize = 11 };
            earlier.Click += (_, _) => { _visibleTurns += 20; RenderConversation(); QueueLayout(); };
            ConversationPanel.Children.Add(earlier);
        }
        foreach (var turn in _session.Turns.TakeLast(_visibleTurns))
        {
            ConversationPanel.Children.Add(new ConversationMessageRow(true, ConversationMessageRow.MessageText(turn.Question, true)));
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(ConversationMessageRow.MessageText(turn.Reply.text, false));
            if (turn.Reply.sources?.Length > 0)
            {
                var links = new StackPanel { Spacing = 2 };
                foreach (var source in turn.Reply.sources)
                {
                    if (!Uri.TryCreate(source.url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) continue;
                    var button = new Button { Content = new TextBlock { Text = $"[{source.id}] {source.title}", TextWrapping = TextWrapping.Wrap }, FontSize = 12, Foreground = Brush.Parse("#96E9F7"), Background = Brushes.Transparent, Padding = new Thickness(4), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left };
                    button.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); } catch (Exception ex) { AppLog.Error("Unable to open an evidence link.", ex); } };
                    links.Children.Add(button);
                }
                var expander = new Expander { Header = LocalizationManager.Text($"來源 · {links.Children.Count}",$"Sources · {links.Children.Count}"), Content = links, FontSize = 12, Foreground = Brush.Parse("#D5DCDC") };
                expander.Expanded += (_, _) => QueueLayout(); expander.Collapsed += (_, _) => QueueLayout();
                content.Children.Add(expander);
            }
            ConversationPanel.Children.Add(new ConversationMessageRow(false, content) { Margin = new Thickness(0, 0, 0, 4) });
        }
        ReplyScroll.IsVisible = _session.Turns.Count > 0;
        ConversationFrame.IsVisible = ReplyScroll.IsVisible;
        Dispatcher.UIThread.Post(() => ReplyScroll.ScrollToEnd(), DispatcherPriority.Loaded);
    }
    private void QueueLayout()
    {
        if (_layoutPending || _disposed || _closing) return;
        _layoutPending = true;
        Dispatcher.UIThread.Post(() => { _layoutPending = false; if (!_disposed&&!_closing) RefreshLayout(); }, DispatcherPriority.Background);
    }
    private Avalonia.Platform.Screen? TargetScreen()
    {
        var all = Screens.All;
        return _settings.MonitorIndex >= 0 && _settings.MonitorIndex < all.Count ? all[_settings.MonitorIndex] : Screens.Primary ?? all.FirstOrDefault();
    }
    private void RefreshLayout()
    {
        var screen = TargetScreen(); double scale = screen?.Scaling > 0 ? screen.Scaling : 1;
        double available = Math.Max(160, (screen?.WorkingArea.Width ?? 1280) / scale - 48);
        var measured = new FormattedText((InputBox.Text ?? "").Replace('\n', ' '), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(InputBox.FontFamily), 15, Brushes.White);
        double bodyWidth = IslandGeometry.Width(_session.Turns.Count > 0 ? Math.Max(560, measured.Width) : measured.Width, available);
        ApplyViewportLayout(bodyWidth, Math.Min(620, (screen?.WorkingArea.Height ?? 900) / scale * .7));
        PositionIsland(); if (IsVisible) EnsureHitTest();
    }
    public void ApplyViewportLayout(double bodyWidth, double maxHeight)
    {
        var measured = new FormattedText(InputBox.Text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(InputBox.FontFamily), 15, Brushes.White);
        Width = bodyWidth + 16; measured.MaxTextWidth = Math.Max(80, bodyWidth - 64);
        ApplyCompactLayout(bodyWidth);
        double contentWidth = Math.Max(80, bodyWidth - 30);
        BodyHeader.Measure(new Size(contentWidth, double.PositiveInfinity));
        FooterGrid.Measure(new Size(contentWidth, double.PositiveInfinity));
        double chrome = BodyHeader.DesiredSize.Height + FooterGrid.DesiredSize.Height + 42 + 10 + (ReplyScroll.IsVisible ? 36 : 0) + (ImagePreviewFrame.IsVisible?88:0);
        InputBox.Height = Math.Clamp(measured.Height + 24, 38, Math.Max(38, Math.Min(180, maxHeight - chrome - (ReplyScroll.IsVisible ? 40 : 0))));
        ReplyScroll.MaxHeight = Math.Max(0, maxHeight - chrome - InputBox.Height);
        var conversationWidth = Math.Max(80, bodyWidth - 88);
        foreach (var row in ConversationPanel.Children.OfType<ConversationMessageRow>()) row.SetAvailableWidth(conversationWidth);
        ConversationPanel.Measure(new Size(conversationWidth, double.PositiveInfinity));
        ReplyScroll.ClearValue(HeightProperty);
        Island.Measure(new Size(bodyWidth, double.PositiveInfinity));
        Height = Math.Min(maxHeight, Math.Max(124, Island.DesiredSize.Height + 16));
    }
    private void PositionIsland()
    {
        var screen = TargetScreen(); if (screen is null) return;
        var area = screen.WorkingArea; var scale = screen.Scaling > 0 ? screen.Scaling : 1;
        Position = new PixelPoint(area.X + (int)Math.Round((area.Width - Width * scale) / 2), area.Y + (int)Math.Round(8 * scale));
    }
    private void EnsureHitTest()
    {
        var handle = this.TryGetPlatformHandle(); if (handle is null) return;
        _hitTest ??= WindowsHudHitTest.TryAttach(handle.Handle, p =>
        {
            var local = this.PointToClient(p);
            return IsVisible && IslandGeometry.Contains(local.X - 8, local.Y - 8, Bounds.Width - 16, Bounds.Height - 16);
        }, () => { }, nativeControls: true);
        _hitTest?.Reapply();
        UpdateInputRegion();
    }
    private void UpdateInputRegion()
        => _paintedRegion.Update(this, Island, _hitTest);
    public void ApplyCompactLayout(double bodyWidth)
    {
        bool compact = bodyWidth < 440;
        FooterGrid.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
        Grid.SetColumnSpan(StatusText, compact ? 3 : 1);
        Grid.SetRow(ModeCombo, compact ? 1 : 0); Grid.SetRow(SendButton, compact ? 1 : 0);
        ModeCombo.Width = compact ? 82 : 96;
        ModeCombo.Margin = compact ? new Thickness(0,8,0,0) : new Thickness(0);
        SendButton.Margin = new Thickness(6,compact ? 8 : 0,0,0);
        SendButton.Padding = compact ? new Thickness(8,4) : new Thickness(12,4);
        HeaderArtwork.IsVisible = bodyWidth >= 300;
    }
}
