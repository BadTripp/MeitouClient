using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meitou.Content;

namespace Meitou.Data.Ui;

/// <summary>Original option descriptions from installed gettext source references; no game prose is embedded.</summary>
public sealed class NewGameHelpCatalog
{
    readonly Dictionary<int, string> descriptions = [];
    public string EnglishAt(int line) => descriptions.GetValueOrDefault(line, "");
    public string TextAt(int line) => Meitou.Localisation.Loc.Tr(EnglishAt(line));

    public static NewGameHelpCatalog Read(string? path)
    {
        var result = new NewGameHelpCatalog();
        if (path is null || !File.Exists(path = GameFiles.Resolve(path))) return result;
        var references = new List<int>();
        var text = new StringBuilder();
        bool reading = false;
        void Finish()
        {
            // A label and its description share a source line; the description is the longer msgid.
            foreach (int line in references)
                if (text.Length > result.EnglishAt(line).Length) result.descriptions[line] = text.ToString();
            references.Clear(); text.Clear(); reading = false;
        }
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0) { Finish(); continue; }
            if (line.StartsWith("#:", StringComparison.Ordinal))
                foreach (Match match in Regex.Matches(line, @"kenshi/titlescreen\.cpp:(\d+)"))
                {
                    int source = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (source is >= 557 and <= 563) references.Add(source);
                }
            if (line.StartsWith("msgid ", StringComparison.Ordinal)) { reading = true; text.Append(JsonSerializer.Deserialize<string>(line[6..])); }
            else if (line.StartsWith("msgstr", StringComparison.Ordinal) || line.StartsWith("msgctxt", StringComparison.Ordinal)) reading = false;
            else if (reading && line.StartsWith('"')) text.Append(JsonSerializer.Deserialize<string>(line));
        }
        Finish();
        return result;
    }
}
