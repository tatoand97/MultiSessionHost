using System.Windows;
using MultiSessionHost.AdminDesktop.ViewModels;

namespace MultiSessionHost.AdminDesktop;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
