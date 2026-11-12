using System.Windows;
using System.Windows.Documents;
using EarthWatch3D.App.ViewModels;

namespace EarthWatch3D.App.Views;

/// <summary>The About window. DataContext: AboutViewModel.</summary>
public partial class AboutWindow : Window
{
    private readonly AboutViewModel _vm;

    public AboutWindow(AboutViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
    }

    // A Hyperlink inside a DataTemplate has no visual-tree ancestors to bind a command through, so the click is routed here.
    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (((Hyperlink)sender).DataContext is Credit credit) _vm.OpenLinkCommand.Execute(credit.Url);
    }
}
