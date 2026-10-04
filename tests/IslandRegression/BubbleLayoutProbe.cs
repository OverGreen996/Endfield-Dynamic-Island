using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using EndfieldChargePlus.Views;

public class BubbleTestApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

public static class BubbleLayoutProbe
{
    public static void Run()
    {
        AppBuilder.Configure<BubbleTestApplication>().UsePlatformDetect().SetupWithoutStarting();
        int passed = 0;
        void Check(bool value, string name)
        { if (!value) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
        foreach (double width in new[] { 160d, 230d, 492d, 720d })
        foreach (bool user in new[] { true, false })
        foreach (string text in new[] { "收到，謝謝！", string.Concat(Enumerable.Repeat("這是一段需要自然換行的長訊息，中英文混合 content。", 8)), "https://example.com/" + new string('a', 600) })
        {
            var row = new ConversationMessageRow(user, ConversationMessageRow.MessageText(text, user));
            row.SetAvailableWidth(width);
            row.Measure(new Size(width, double.PositiveInfinity));
            row.Arrange(new Rect(0, 0, width, row.DesiredSize.Height));
            var bubble = (Border)row.Children[0]; var avatar = (Border)row.Children[1];
            if(width==160&&text.Length<100)
                Check((avatar.BorderBrush as ISolidColorBrush)?.Color==Color.Parse(user?"#E6E744":"#13C8EB"),user?"your avatar uses yellow ring":"AI retains original cyan ring");
            Check(double.IsFinite(row.DesiredSize.Height) && row.DesiredSize.Height > 0 &&
                  bubble.Bounds.X >= -.1 && bubble.Bounds.Right <= width + .1 &&
                  avatar.Bounds.X >= -.1 && avatar.Bounds.Right <= width + .1 &&
                  (user ? bubble.Bounds.Right <= avatar.Bounds.X : avatar.Bounds.Right <= bubble.Bounds.X) &&
                  (text.Length < 100 || bubble.Bounds.Height > 60),
                  $"{(user ? "user/right" : "AI/left")} width={width} chars={text.Length}: wrap and bounds");
        }
        static double Luminance(string color)
        {
            var c = Color.Parse(color);
            double Channel(byte b) { double v = b / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        }
        foreach (var colors in new[] { (ConversationMessageRow.UserSurface, ConversationMessageRow.UserText), (ConversationMessageRow.AssistantSurface, ConversationMessageRow.AssistantText) })
        {
            double a = Luminance(colors.Item1), b = Luminance(colors.Item2);
            Check((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) >= 4.5, "message foreground/background contrast >= 4.5:1");
        }
        Console.WriteLine($"{passed}/{passed} PASS; actual Avalonia measurements, no model or search calls.");
    }
}
