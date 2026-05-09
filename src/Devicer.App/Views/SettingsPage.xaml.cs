using System.Windows.Controls;
using System.Windows.Navigation;
using Devicer.App.Services;

namespace Devicer.App.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        // Route through UrlLauncher for the same http/https-only safety net the rest of the
        // app uses; even though the hyperlink target is hard-coded in XAML today, the gate
        // costs nothing and prevents future copies of this handler from regressing.
        UrlLauncher.TryOpen(e.Uri);
        e.Handled = true;
    }
}
