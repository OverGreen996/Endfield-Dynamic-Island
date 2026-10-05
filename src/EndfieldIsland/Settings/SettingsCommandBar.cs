using Avalonia;
using Avalonia.Controls;

namespace EndfieldChargePlus.Settings;

/// <summary>Reflows the settings commands from their actual available space, including DPI scaling.</summary>
public sealed class SettingsCommandBar : Grid
{
    protected override Size MeasureOverride(Size availableSize)
    {
        bool stacked = availableSize.Width < 712;
        if ((RowDefinitions.Count > 1) != stacked)
        {
            ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "*,Auto");
            RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "Auto");
        }
        var actions = Children.OfType<WrapPanel>().FirstOrDefault();
        if (actions is not null)
        {
            SetColumn(actions, stacked ? 0 : 1);
            SetRow(actions, stacked ? 1 : 0);
            actions.MaxWidth = Math.Max(80, availableSize.Width - (stacked ? 0 : 140));
            actions.Margin = stacked ? new Thickness(0, 10, 0, 0) : default;
        }
        return base.MeasureOverride(availableSize);
    }
}
