using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Vectors.EuroScopeUpdater.App.Views;

/// <summary>true → Visible, false → Collapsed. Pass parameter "invert" to reverse.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
    {
        var b = value is true;
        if (parameter as string == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object parameter, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Inverts a boolean.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c) => value is not true;
    public object ConvertBack(object value, Type t, object parameter, CultureInfo c) => value is not true;
}

/// <summary>Maps a FIR status "kind" (ok/warn/danger/muted) to a themed brush.</summary>
public sealed class StatusKindToBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
    {
        var key = (value as string) switch
        {
            "ok" => "RadarBlipBrush",
            "warn" => "WarningBrush",
            "danger" => "DangerBrush",
            _ => "MutedBrush",
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object parameter, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Visible when the bound WizardStep equals the ConverterParameter step name.</summary>
public sealed class EnumEqualsVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object parameter, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Highlights the active step in the wizard progress rail.</summary>
public sealed class StepActiveBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
    {
        var current = System.Convert.ToInt32(value);
        var index = System.Convert.ToInt32(parameter);
        var key = index <= current ? "PrimaryBrush" : "BorderBrush";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object parameter, CultureInfo c) => throw new NotSupportedException();
}
