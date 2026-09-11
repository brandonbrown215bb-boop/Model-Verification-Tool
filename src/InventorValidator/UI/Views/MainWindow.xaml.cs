using System.ComponentModel;
using System.Windows;
using InventorValidator.UI.ViewModels;

namespace InventorValidator.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is MainViewModel vm)
        {
            vm.OnWindowClosing();
        }
    }
}
