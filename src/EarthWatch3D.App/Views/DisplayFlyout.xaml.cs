using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace EarthWatch3D.App.Views;

/// <summary>The ⋯ Display flyout's content. DataContext: DisplayOptionsViewModel.</summary>
public partial class DisplayFlyout : UserControl
{
    public DisplayFlyout() => InitializeComponent();
}

/// <summary>A (Label, Value) choice → its label (WPF cannot bind to tuple fields).</summary>
public sealed class ChoiceLabelConverter : IValueConverter
{
    public static readonly ChoiceLabelConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ValueTuple<string, double> c ? c.Item1 : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Quake window days ↔ chip index: 1 ↔ 0 ("24 h"), 7 ↔ 1 ("7 days").</summary>
public sealed class WindowDaysIndexConverter : IValueConverter
{
    public static readonly WindowDaysIndexConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is 7 ? 1 : 0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is 1 ? 7 : value is 0 ? 1 : Binding.DoNothing;
}
