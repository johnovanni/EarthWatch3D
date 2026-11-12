using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using EarthWatch3D.Core.Feeds;

namespace EarthWatch3D.App.Views;

/// <summary>The status dots in the time bar. DataContext: StatusViewModel.</summary>
public partial class StatusStrip : UserControl
{
    public StatusStrip() => InitializeComponent();
}

/// <summary>Ok green, Retrying amber, Failing red, Paused blue, NotSetUp / NotStarted grey.</summary>
public sealed class FeedStateBrushConverter : IValueConverter
{
    public static readonly FeedStateBrushConverter Instance = new();
    public static readonly SolidColorBrush Ok = Frozen(0x34, 0xC7, 0x59);
    public static readonly SolidColorBrush Retrying = Frozen(0xFF, 0xCC, 0x00);
    public static readonly SolidColorBrush Failing = Frozen(0xFF, 0x3B, 0x30);
    public static readonly SolidColorBrush Paused = Frozen(0x0A, 0x84, 0xFF);
    public static readonly SolidColorBrush Idle = Frozen(0x8E, 0x8E, 0x93);

    public static SolidColorBrush For(FeedState s) => s switch
    {
        FeedState.Ok => Ok,
        FeedState.Retrying => Retrying,
        FeedState.Failing => Failing,
        FeedState.Paused => Paused,
        _ => Idle,
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is FeedState s ? For(s) : Idle;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
