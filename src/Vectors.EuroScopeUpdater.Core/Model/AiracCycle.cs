using System.Globalization;

namespace Vectors.EuroScopeUpdater.Core.Model;

/// <summary>
/// A VATSIM/AIRAC cycle identifier in the AeroNav 4-digit <c>YYNN</c> form
/// (e.g. <c>2608</c> = year 2026, cycle 08). Chronological order is the natural
/// numeric order of the 4-digit value.
/// </summary>
public readonly record struct AiracCycle : IComparable<AiracCycle>
{
    /// <summary>Raw 4-digit value, e.g. 2608.</summary>
    public int Value { get; }

    public AiracCycle(int value)
    {
        if (value is < 100 or > 9913)
            throw new ArgumentOutOfRangeException(nameof(value), value, "AIRAC cycle must be a 4-digit YYNN value.");
        Value = value;
    }

    public int Year => Value / 100;      // e.g. 26
    public int Number => Value % 100;    // e.g. 8

    public static bool TryParse(string? text, out AiracCycle cycle)
    {
        cycle = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.Length != 4) return false;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var v)) return false;
        var n = v % 100;
        if (n is < 1 or > 13) return false;   // AIRAC cycles run 01..13 within a year
        cycle = new AiracCycle(v);
        return true;
    }

    public int CompareTo(AiracCycle other) => Value.CompareTo(other.Value);
    public static bool operator <(AiracCycle a, AiracCycle b) => a.Value < b.Value;
    public static bool operator >(AiracCycle a, AiracCycle b) => a.Value > b.Value;
    public static bool operator <=(AiracCycle a, AiracCycle b) => a.Value <= b.Value;
    public static bool operator >=(AiracCycle a, AiracCycle b) => a.Value >= b.Value;

    public override string ToString() => Value.ToString("D4", CultureInfo.InvariantCulture);
}
