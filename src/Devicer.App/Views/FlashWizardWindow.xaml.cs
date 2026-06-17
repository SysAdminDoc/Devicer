using System.Windows;

namespace Devicer.App.Views;

public partial class FlashWizardWindow : Window
{
    public FlashWizardWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
