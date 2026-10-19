namespace Meitou.Data.Characters;

/// <summary>
/// The random first names of characters whose CHARACTER is <c>named</c> (docs/characters.md "Names"): <c>data/namesM.txt</c> for men,
/// <c>data/namesF.txt</c> for women, <c>data/namesMF.txt</c> added to both; lines trimmed, blank ones skipped, a missing file adds nothing.
/// </summary>
public sealed class NamePool
{
    public IReadOnlyList<string> Male { get; }
    public IReadOnlyList<string> Female { get; }

    public NamePool(IEnumerable<string> male, IEnumerable<string> female, IEnumerable<string> both)
    {
        var shared = both.ToList();
        Male = [.. male, .. shared];
        Female = [.. female, .. shared];
    }

    public static NamePool Load(string installRoot)
    {
        string data = Path.Combine(installRoot, "data");
        return new NamePool(Lines(Path.Combine(data, "namesM.txt")), Lines(Path.Combine(data, "namesF.txt")), Lines(Path.Combine(data, "namesMF.txt")));
    }

    static IEnumerable<string> Lines(string path)
    {
        try { return [.. File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0)]; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>A name from the sex's list picked by <paramref name="roll"/> (uniform over the list), or null when the list is empty.</summary>
    public string? Pick(bool female, ulong roll)
    {
        var list = female ? Female : Male;
        return list.Count == 0 ? null : list[(int)(roll % (ulong)list.Count)];
    }
}
