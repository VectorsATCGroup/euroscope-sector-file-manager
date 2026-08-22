using System.ComponentModel;
using System.Windows;
using Vectors.EuroScopeUpdater.App.ViewModels;

namespace Vectors.EuroScopeUpdater.App.Views;

public partial class UpdateDialog : Window
{
    public UpdateDialog(UpdateViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestClose += (_, _) => Close();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // While a download is running the window close button acts like "Cancel".
        if (DataContext is UpdateViewModel { Busy: true } vm)
        {
            e.Cancel = true;
            vm.LaterCommand.Execute(null);
        }
    }
}
