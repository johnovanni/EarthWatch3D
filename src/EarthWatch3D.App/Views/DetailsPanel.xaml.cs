using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EarthWatch3D.App.ViewModels;

namespace EarthWatch3D.App.Views;

public partial class DetailsPanel : UserControl
{
    public DetailsPanel() => InitializeComponent();

    private void OnSourceLinkClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SelectionViewModel { Link: { } link }) OpenInBrowser(link);
    }

    /// <summary>Opens a web page in the default browser. Only http(s) links are opened.</summary>
    public static void OpenInBrowser(Uri link)
    {
        if (link.Scheme != Uri.UriSchemeHttp && link.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}

public sealed record DetailRow(string Label, string Value);

/// <summary>Turns the view model's (Label, Value) tuples, whose members are fields, into rows WPF can bind to.</summary>
public sealed class LinesToRowsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is IReadOnlyList<(string Label, string Value)> lines
            ? lines.Select(l => new DetailRow(l.Label, l.Value)).ToList()
            : new List<DetailRow>();
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public static readonly NullToCollapsedConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
