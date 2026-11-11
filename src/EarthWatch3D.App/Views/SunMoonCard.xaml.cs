using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace EarthWatch3D.App.Views;

public partial class SunMoonCard : UserControl
{
    public SunMoonCard() => InitializeComponent();
}

/// <summary>IsCollapsed -> chevron glyph: up (collapse) when expanded, down (expand) when collapsed.</summary>
public sealed class ChevronConverter : IValueConverter
{
    public static readonly ChevronConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "▾" : "▴";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PhaseConverter : IValueConverter
{
    public static readonly PhaseConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "waxing" : "waning";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
