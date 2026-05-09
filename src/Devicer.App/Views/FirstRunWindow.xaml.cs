using System.Windows;
using Devicer.App.ViewModels;

namespace Devicer.App.Views;

public partial class FirstRunWindow : Window
{
    private readonly FirstRunViewModel _vm;

    public FirstRunWindow(FirstRunViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        _vm.CompleteCommand.Execute(null);
        DialogResult = true;
        Close();
    }
}
