using CommunityToolkit.Mvvm.ComponentModel;
using Vectors.EuroScopeUpdater.Core.Operations;

namespace Vectors.EuroScopeUpdater.App.ViewModels;

/// <summary>Live progress of an in-flight install/update, shown as a modal overlay.</summary>
public sealed partial class OperationViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _phaseText = "";
    [ObservableProperty] private double _percent;
    [ObservableProperty] private bool _indeterminate = true;
    [ObservableProperty] private string? _bytesText;
    [ObservableProperty] private bool _isVisible;
    /// <summary>Cancellation is only safe before the commit swap; disabled afterwards.</summary>
    [ObservableProperty] private bool _canCancel = true;

    /// <summary>Reset for a new operation.</summary>
    public void Reset(string title)
    {
        Title = title;
        PhaseText = "";
        Percent = 0;
        Indeterminate = true;
        BytesText = null;
        CanCancel = true;
    }

    private static Services.Localization Loc => Services.Localization.Instance;

    private static string PhaseLabel(OperationPhase phase) => phase switch
    {
        OperationPhase.Prepare => Loc.T("Op_Prepare"),
        OperationPhase.Download => Loc.T("Op_Download"),
        OperationPhase.ValidateArchive => Loc.T("Op_ValidateArchive"),
        OperationPhase.Stage => Loc.T("Op_Stage"),
        OperationPhase.ValidateStaging => Loc.T("Op_ValidateStaging"),
        OperationPhase.Backup => Loc.T("Op_Backup"),
        OperationPhase.Commit => Loc.T("Op_Commit"),
        OperationPhase.Verify => Loc.T("Op_Verify"),
        OperationPhase.RollingBack => Loc.T("Op_RollingBack"),
        _ => "",
    };

    public void Update(OperationProgress p)
    {
        PhaseText = PhaseLabel(p.Phase);
        if (p.Fraction is { } f)
        {
            Indeterminate = false;
            Percent = Math.Round(f * 100);
        }
        else
        {
            Indeterminate = true;
        }

        // Once committing/verifying, the operation can no longer be safely cancelled.
        CanCancel = p.Phase is OperationPhase.Prepare or OperationPhase.Download
            or OperationPhase.ValidateArchive or OperationPhase.Stage
            or OperationPhase.ValidateStaging or OperationPhase.Backup;

        BytesText = p is { BytesReceived: { } r, TotalBytes: { } t } && t > 0
            ? $"{Mb(r):0.0} MB / {Mb(t):0.0} MB"
            : null;
    }

    private static double Mb(long bytes) => bytes / 1_000_000.0;
}
