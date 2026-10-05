using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;
using System.Reflection;

// A settings-only fixture. It never activates an island, saves preferences, or calls a model.
public sealed class SettingsChromeApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://EndfieldChargePlus")) { Source = new Uri("avares://EndfieldChargePlus/Styles/IndustrialTheme.axaml") });
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        LocalizationManager.SetLanguage(AppLanguage.TraditionalChinese);
        var hud = new HudWindow();
        var runtime = new CustomHudRuntime(hud);
        var settings = new SettingsWindow(new AppSettings(), hud, runtime, _ => { }, _ => { }) { Title = "終末地 設定介面驗收" };
        desktop.MainWindow = settings;
        settings.Show();
        desktop.Exit += (_, _) => { hud.Close(); runtime.Dispose(); };
        if (desktop.Args?.Contains("--settings-ui") == true) { base.OnFrameworkInitializationCompleted(); return; }
        Dispatcher.UIThread.Post(async () =>
        {
            int passed = 0;
            void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
            var folder = Path.Combine(Environment.CurrentDirectory, "outputs", "Settings-Integrated-Chrome");
            Directory.CreateDirectory(folder);
            var root = (Control)settings.Content!;
            var tabs = settings.FindControl<TabControl>("ModuleTabs")!;
            try
            {
                await Task.Delay(250);
                Check(settings.IsExtendedIntoWindowDecorations, "settings content occupies native titlebar area");
                Check(settings.CanResize && settings.SystemDecorations == SystemDecorations.Full, "native resize frame retained");
                foreach (var language in new[] { AppLanguage.TraditionalChinese, AppLanguage.English })
                {
                    LocalizationManager.SetLanguage(language);
                    typeof(SettingsWindow).GetMethod("ApplyLocalization", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(settings, null);
                    foreach (double width in new[] { 1120d, 940d, 720d })
                    {
                        settings.MinWidth = 0;
                        settings.Width = width;
                        await Task.Delay(120);
                        foreach (int tab in Enumerable.Range(0, 6))
                        {
                            tabs.SelectedIndex = tab;
                            await Task.Delay(70);
                            Check(settings.FindControl<TextBlock>("SettingsSectionTitle")!.Text == ((TabItem)tabs.Items[tab]!).Header as string, $"{language}/{width}/{tab}: section title follows active tab");
                            Check(tabs.Bounds.Left >= 0 && tabs.Bounds.Right <= settings.ClientSize.Width + 1, $"{language}/{width}/{tab}: content fits window");
                            if (width >= 940 && tab < 4)
                            {
                                var content = (Control)((TabItem)tabs.Items[tab]!).Content!;
                                foreach (var field in content.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Name is not null && c is Button or TextBox or ComboBox or ToggleSwitch or NumericUpDown))
                                {
                                    var p = field.TranslatePoint(default, root)!.Value;
                                    Check(p.X >= 0 && p.X + field.Bounds.Width <= settings.ClientSize.Width + 1, $"{language}/{width}/{tab}: setting control remains in viewport {field.Name}");
                                }
                            }
                            if (width == 1120 || tab == 0) Save(settings, folder, $"settings-{language}-{width}-{tab}", 1);
                        }
                        Rect previous = default;
                        foreach (var name in new[] { "MinimizeSettingsBtn", "MaximizeSettingsBtn", "CloseSettingsBtn" })
                        {
                            var button = settings.FindControl<Button>(name)!;
                            var p = button.TranslatePoint(default, root)!.Value;
                            var rect = new Rect(p, button.Bounds.Size);
                            Check(rect.Left >= previous.Right && rect.Right <= settings.ClientSize.Width + 1 && rect.Top >= 0 && rect.Height >= 40, $"{language}/{width}: caption fits without overlap {name}");
                            Check(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)), $"{language}/{width}: accessible caption {name}");
                            previous = rect;
                        }
                        foreach (var name in new[] { "LanguageChineseBtn", "LanguageEnglishBtn", "SaveBtn", "OpenSettingsFolderBtn" })
                        {
                            var button = settings.FindControl<Button>(name)!;
                            var p = button.TranslatePoint(default, root)!.Value;
                            Check(p.X >= 0 && p.X + button.Bounds.Width <= settings.ClientSize.Width + 1 && p.Y + button.Bounds.Height <= tabs.Bounds.Y, $"{language}/{width}: command fits above content {name}");
                        }
                    }
                }
                settings.Width = 1120;
                var maximize = settings.FindControl<Button>("MaximizeSettingsBtn")!;
                maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(200);
                Check(settings.WindowState == WindowState.Maximized, "caption maximizes native window");
                Check(AutomationProperties.GetName(maximize) == "Restore window", "maximized caption exposes restore action");
                Check(root.Bounds.Width <= settings.ClientSize.Width + 1, "maximized content stays inside work area");
                maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(180);
                Check(settings.WindowState == WindowState.Normal, "caption restores native window");
                settings.FindControl<Button>("MinimizeSettingsBtn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(120);
                Check(settings.WindowState == WindowState.Minimized, "caption minimizes native window");
                settings.WindowState = WindowState.Normal;
                await Task.Delay(100);
                tabs.SelectedIndex = 0;
                Save(settings, folder, "settings-English-150percent", 1.5);
                Save(settings, folder, "settings-English-200percent", 2);
                bool closed = false;
                settings.Closed += (_, _) => closed = true;
                settings.FindControl<Button>("CloseSettingsBtn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(closed, "caption closes settings only");
                Console.WriteLine($"{passed}/{passed} PASS");
                Environment.ExitCode = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { desktop.Shutdown(Environment.ExitCode); }
        });
        base.OnFrameworkInitializationCompleted();
    }

    private static void Save(Window window, string folder, string name, double scale)
    {
        var content = (Control)window.Content!;
        using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(content.Bounds.Width * scale), (int)Math.Ceiling(content.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        image.Render(content);
        image.Save(Path.Combine(folder, name + ".png"));
    }
}
