using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EndfieldChargePlus.Assistant;

namespace EndfieldChargePlus.Views;

/// <summary>One role-aligned message; presentation only, with no network or history ownership.</summary>
public sealed class ConversationMessageRow : Grid
{
    public const string UserSurface = "#F1F0EE";
    public const string UserText = "#202424";
    public const string AssistantSurface = "#414444";
    public const string AssistantText = "#F2F2EF";
    public const string Accent = "#13C8EB";
    private readonly Border _bubble;
    private readonly bool _user;

    public ConversationMessageRow(bool user, Control content,AvatarStore? avatars=null)
    {
        _user = user;
        ColumnDefinitions = new ColumnDefinitions(user ? "*,Auto" : "Auto,*");
        HorizontalAlignment = HorizontalAlignment.Stretch;
        var avatar = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15),
            BorderBrush = Brush.Parse(user?"#E6E744":Accent), BorderThickness = new Thickness(1.5),Padding=new Thickness(2),
            Background = Brush.Parse("#222828"), VerticalAlignment = VerticalAlignment.Top,
            Margin = user ? new Thickness(8, 4, 0, 0) : new Thickness(0, 4, 8, 0),
            Child = new TextBlock
            {
                Text = user ? LocalizationManager.Text("你","You") : "AI", FontSize = 10, FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse(user ? UserSurface : Accent),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        if((avatars??AvatarStore.Shared).Image(user) is {}photo)
            avatar.Child=new Border{CornerRadius=new CornerRadius(13),ClipToBounds=true,Child=new Image{Source=photo,Stretch=Stretch.UniformToFill}};
        _bubble = new Border
        {
            Child = content, Padding = new Thickness(13, 10),
            Background = Brush.Parse(user ? UserSurface : AssistantSurface),
            BorderBrush = Brush.Parse(user ? "#D2D4D1" : "#575C5C"), BorderThickness = new Thickness(1),
            CornerRadius = user ? new CornerRadius(14, 3, 14, 14) : new CornerRadius(3, 14, 14, 14),
            HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        AutomationProperties.SetName(_bubble, user ? LocalizationManager.Text("你的訊息","Your message") : LocalizationManager.Text("AI 回覆","AI response"));
        Grid.SetColumn(avatar, user ? 1 : 0);
        Grid.SetColumn(_bubble, user ? 0 : 1);
        Children.Add(_bubble); Children.Add(avatar);
        SetAvailableWidth(500);
    }

    public void SetAvailableWidth(double available)
    {
        // Leave room for the identity ring and an opposite-side gutter even on a narrow display.
        _bubble.MaxWidth = Math.Max(40, Math.Min(available * (_user ? .82 : .90), available - 54));
    }

    public static SelectableTextBlock MessageText(string text, bool user) => new()
    {
        Text = text, Foreground = Brush.Parse(user ? UserText : AssistantText),
        FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 21
    };
}
