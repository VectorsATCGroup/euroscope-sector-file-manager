using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectors.EuroScopeUpdater.Core.Model;
using Vectors.EuroScopeUpdater.Core.Operations;

namespace Vectors.EuroScopeUpdater.App.ViewModels;

/// <summary>One FIR row on the dashboard, with its status and contextual action.</summary>
public sealed partial class FirItemViewModel : ObservableObject
{
    private readonly Func<FirItemViewModel, OperationKind, Task> _run;

    public Fir Fir { get; }
    public string Code => Fir.Code;
    public string Name => Fir.Name;

    [ObservableProperty] private InstallStatus _status = InstallStatus.NotInstalled;
    [ObservableProperty] private AiracCycle? _installedAirac;
    [ObservableProperty] private AiracCycle? _availableAirac;
    [ObservableProperty] private bool _busy;

    public FirItemViewModel(Fir fir, Func<FirItemViewModel, OperationKind, Task> run)
    {
        Fir = fir;
        _run = run;
    }

    public RemotePackage? BestInstall { get; set; }
    public RemotePackage? BestUpdate { get; set; }

    private static Services.Localization Loc => Services.Localization.Instance;

    public string StatusText => Status switch
    {
        InstallStatus.NotInstalled => Loc.T("Fir_NotInstalled"),
        InstallStatus.UpToDate => Loc.T("Fir_UpToDate"),
        InstallStatus.UpdateAvailable => Loc.T("Fir_UpdateAvailable"),
        InstallStatus.LocallyModified => Loc.T("Fir_LocallyModified"),
        InstallStatus.InstallationIncomplete => Loc.T("Fir_Incomplete"),
        InstallStatus.InstalledVersionUnknown when InstalledAirac is not null => string.Format(Loc.T("Fir_InstalledAirac"), InstalledAirac),
        _ => Loc.T("Fir_VersionUnknown"),
    };

    /// <summary>Category used by the UI to pick a status colour.</summary>
    public string StatusKind => Status switch
    {
        InstallStatus.UpToDate => "ok",
        InstallStatus.UpdateAvailable => "warn",
        InstallStatus.NotInstalled => "muted",
        InstallStatus.LocallyModified => "warn",
        InstallStatus.InstallationIncomplete => "danger",
        _ => "muted",
    };

    public string InstalledText => InstalledAirac is { } a ? a.ToString() : "—";
    public string AvailableText => AvailableAirac is { } a ? a.ToString() : "—";

    // Button rules:
    //  • "Instalar" appears ONLY when nothing is installed for this FIR.
    //  • "Atualizar" appears ONLY when an older version is installed and a newer one is available.
    //  • When the installed version equals the available one, no button — the chip shows "Atualizado".
    public bool CanInstall => (Status is InstallStatus.NotInstalled or InstallStatus.InstallationIncomplete) && BestInstall is not null;
    public bool CanUpdate => Status == InstallStatus.UpdateAvailable && (BestUpdate is not null || BestInstall is not null);

    /// <summary>Whether the single contextual action button is shown at all.</summary>
    public bool ShowPrimaryAction => CanInstall || CanUpdate;

    public string PrimaryActionText => CanUpdate ? Loc.T("Fir_Update") : Loc.T("Fir_Install");

    public void Refreshed()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(InstalledText));
        OnPropertyChanged(nameof(AvailableText));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanUpdate));
        OnPropertyChanged(nameof(ShowPrimaryAction));
        OnPropertyChanged(nameof(PrimaryActionText));
        PrimaryActionCommand.NotifyCanExecuteChanged();
    }

    private bool CanAct() => !Busy && ShowPrimaryAction;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task PrimaryAction() =>
        _run(this, CanUpdate ? OperationKind.Update : OperationKind.CleanInstall);
}
