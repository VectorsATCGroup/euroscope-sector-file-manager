using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace Vectors.EuroScopeUpdater.App.Services;

/// <summary>
/// XAML markup extension: <c>{loc:Loc SomeKey}</c> binds to <see cref="Localization"/>'s indexer, so the
/// text updates live when the language changes.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = Localization.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
