using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Vectors.EuroScopeUpdater.App.Services;

public interface IDialogService
{
    void Info(string title, string message);
    void Error(string title, string message);
    bool Confirm(string title, string message);
    string? PickFolder(string description, string? initialDirectory = null);
}

public sealed class DialogService : IDialogService
{
    public void Info(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Error(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? PickFolder(string description, string? initialDirectory = null)
    {
        var dlg = new OpenFolderDialog { Title = description };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dlg.InitialDirectory = initialDirectory;
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }
}
