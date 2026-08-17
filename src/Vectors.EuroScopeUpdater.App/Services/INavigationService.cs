namespace Vectors.EuroScopeUpdater.App.Services;

/// <summary>Top-level navigation between the wizard, dashboard and settings.</summary>
public interface INavigationService
{
    void GoToDashboard();
    void GoToWizard();
    void GoToSettings();
}
