using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Devicer.App.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true,
            });
        }
        catch { /* user can right-click copy if shell launch fails */ }
        e.Handled = true;
    }
}
