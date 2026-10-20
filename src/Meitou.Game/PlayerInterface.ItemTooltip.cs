using System.Globalization;
using Meitou.Data.Gameplay.Combat;
using Meitou.Simulation.Combat;

namespace Meitou.Game;

sealed partial class PlayerInterface
{
    /// <summary>The ARMOUR record of an item, null for anything else (the host sets it).</summary>
    public Func<string, ArmourData?>? ArmourOf { get; set; }
    /// <summary>The name of a record by string id (a body part of an armour's coverage); the host sets it.</summary>
    public Func<string, string?>? RecordName { get; set; }

    /// <summary>The grade colours (<c>ItemGrade0</c> to <c>ItemGrade4</c> of kenshi_colours.xml; Specialist and Masterwork share the last).</summary>
    internal static string GradeColour(int grade) => grade switch
    {
        0 => "0.4 0.4 0.4", 1 => "0.851 0.8 0.749", 2 => "0.251 0.6 0.702", 3 => "0.4 0.988 0.502", _ => "0.988 0.922 0.051",
    };

    /// <summary>
    /// The tooltip lines of a piece of armour (docs/game/ui-screens.md "Item tooltip"): grade, class and material, resistances, weight, the trade lines when there are any
    /// (<paramref name="trade"/>, whose own weight line is dropped), coverage and skill effects. A line is <c>label</c>, <c>label\tvalue</c> or <c>label\tvalue\tkind</c> (see <see cref="TooltipLine"/>);
    /// null when the item is not armour.
    /// </summary>
    string? ArmourCaption(string record, float quality, float weight, string? trade)
    {
        if (ArmourOf?.Invoke(record) is not { } armour) return null;
        var lines = new List<string>();
        lines.AddRange(ArmourTooltip.Head(armour, quality).Select(TooltipLine));
        lines.Add("-Weight\t" + weight.ToString("0.##", CultureInfo.InvariantCulture) + " kg");
        if (trade is not null)
            foreach (var line in trade.Split('\n').Where(l => !l.StartsWith("Weight\t", StringComparison.Ordinal)))
                lines.Add(line.Contains('\t') ? "-" + line : line);
        lines.AddRange(ArmourTooltip.Tail(armour, id => RecordName?.Invoke(id)).Select(TooltipLine));
        return string.Join('\n', lines);
    }

    /// <summary>A tooltip line as text: a tab between label and value, a third field for its colour (<c>s</c> a statistic's label, <c>g0</c> to <c>g5</c> a grade's name).</summary>
    static string TooltipLine(ArmourTooltip.Line line) => line.Kind switch
    {
        ArmourTooltip.LineKind.Grade => $"{line.Label}\t\tg{line.Grade}",
        ArmourTooltip.LineKind.Stat => $"{line.Label}\t{line.Value}\ts",
        _ => line.Value.Length > 0 ? $"{line.Label}\t{line.Value}" : line.Label,
    };

    /// <summary>The tooltip row of a line written by <see cref="TooltipLine"/>.</summary>
    static HudLine TooltipRow(string line)
    {
        var parts = line.Split('\t');
        return parts switch
        {
            [var label, var value, "s"] => new(label, value, TitleColour),
            [var label, _, ['g', ..] grade] when int.TryParse(grade.AsSpan(1), CultureInfo.InvariantCulture, out int g) => new(label, "", GradeColour(g)),
            [var label, var value, ..] => new(label, value),
            _ => new(line, ""),
        };
    }
}
