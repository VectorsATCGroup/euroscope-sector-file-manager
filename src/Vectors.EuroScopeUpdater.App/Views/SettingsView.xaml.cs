using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Vectors.EuroScopeUpdater.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch { /* ignore — opening the browser is best-effort */ }
        e.Handled = true;
    }
}
