using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace EarthWatch3D.App.Views;

/// <summary>Top-left layer toggles, with the ⋯ Display flyout. DataContext: MainViewModel.</summary>
public partial class LayerStrip : UserControl
{
    public LayerStrip() => InitializeComponent();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
