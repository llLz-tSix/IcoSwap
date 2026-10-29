using System.Text;
using System.Text.RegularExpressions;

namespace IcoSwap.Core.Matching;

/// <summary>Нормализованное имя: токены, слитная форма и канонический ключ из словаря синонимов.</summary>
public sealed record NameInfo(string Raw, IReadOnlyList<string> Tokens, string Compact, string? Canonical)
{
    public static NameInfo From(string raw) => NameNormalizer.Analyze(raw);
}

public static partial class NameNormalizer
{
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "icon", "icons", "ico", "logo", "app", "application", "shortcut", "lnk", "symbol",
        "ярлык", "значок", "иконка", "приложение", "x64", "x86", "64bit", "32bit",
    };

    [GeneratedRegex(@"(?<=[a-zа-яё])(?=[A-ZА-ЯЁ])")]
    private static partial Regex CamelBoundary();

    [GeneratedRegex(@"^(x?\d{1,4}(x\d{1,4})?(px)?|\d{1,4}px|\d{1,2}x)$")]
    private static partial Regex SizeToken();

    public static NameInfo Analyze(string raw)
    {
        var tokens = Tokenize(raw);
        var compact = string.Concat(tokens);
        return new NameInfo(raw, tokens, compact, Synonyms.Lookup(compact));
    }

    public static List<string> Tokenize(string raw)
    {
        var s = CamelBoundary().Replace(raw, " ");
        s = s.ToLowerInvariant().Replace('ё', 'е')
            .Replace("++", "pp").Replace("c#", "csharp").Replace("f#", "fsharp").Replace(".net", "dotnet");
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        var tokens = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // размеры в конце имени: chrome_256, folder-48x48, icon@2x
        while (tokens.Count > 1 && SizeToken().IsMatch(tokens[^1])) tokens.RemoveAt(tokens.Count - 1);
        var cleaned = tokens.Where(t => !Noise.Contains(t)).ToList();
        return cleaned.Count > 0 ? cleaned : tokens;
    }

    public static string Compact(string raw) => string.Concat(Tokenize(raw));

    /// <summary>Сходство Джаро — Винклера (0..1).</summary>
    public static double JaroWinkler(string a, string b)
    {
        if (a == b) return 1;
        if (a.Length == 0 || b.Length == 0) return 0;
        int range = Math.Max(0, Math.Max(a.Length, b.Length) / 2 - 1);
        var am = new bool[a.Length];
        var bm = new bool[b.Length];
        int matches = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int lo = Math.Max(0, i - range), hi = Math.Min(b.Length - 1, i + range);
            for (int j = lo; j <= hi; j++)
            {
                if (bm[j] || a[i] != b[j]) continue;
                am[i] = bm[j] = true;
                matches++;
                break;
            }
        }
        if (matches == 0) return 0;
        int t = 0, k = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (!am[i]) continue;
            while (!bm[k]) k++;
            if (a[i] != b[k]) t++;
            k++;
        }
        double m = matches;
        double jaro = (m / a.Length + m / b.Length + (m - t / 2.0) / m) / 3.0;
        int prefix = 0;
        while (prefix < Math.Min(4, Math.Min(a.Length, b.Length)) && a[prefix] == b[prefix]) prefix++;
        return jaro + prefix * 0.1 * (1 - jaro);
    }
}
