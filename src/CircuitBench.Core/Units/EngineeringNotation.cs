using System.Globalization;

namespace CircuitBench.Core.Units;

public readonly record struct ParseError(int Position, string MessageZh);

/// <summary>
/// 工程記號解析：數字 + 可選字尾（f p n u µ m k meg g t，不分大小寫）+ 可選單位字母（忽略）。
/// m 一律為毫，百萬必須寫 meg。
/// </summary>
public static class EngineeringNotation
{
    private static readonly (int Exponent, string Suffix)[] FormatSuffixes =
    [
        (-15, "f"), (-12, "p"), (-9, "n"), (-6, "µ"), (-3, "m"), (0, ""), (3, "k"), (6, "meg"), (9, "g"), (12, "t"),
    ];

    public static bool TryParse(string? text, out double value, out ParseError error)
    {
        value = 0;
        error = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = new ParseError(0, "數值不可為空白。");
            return false;
        }

        var s = text.Trim();
        var offset = text.IndexOf(s[0]);
        var i = 0;

        if (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            i++;
        }

        var mantissaDigits = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; mantissaDigits++; }
        if (i < s.Length && s[i] == '.')
        {
            i++;
            while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; mantissaDigits++; }
        }

        if (mantissaDigits == 0)
        {
            error = new ParseError(offset + i, $"「{text.Trim()}」不是有效的數值：第 {offset + i + 1} 個字元應為數字。");
            return false;
        }

        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            var expStart = i;
            i++;
            if (i < s.Length && (s[i] == '+' || s[i] == '-'))
            {
                i++;
            }

            var expDigits = 0;
            while (i < s.Length && char.IsAsciiDigit(s[i])) { i++; expDigits++; }
            if (expDigits == 0)
            {
                error = new ParseError(offset + expStart, $"「{text.Trim()}」的科學記號缺少指數數字。");
                return false;
            }
        }

        var number = double.Parse(s.AsSpan(0, i), NumberStyles.Float, CultureInfo.InvariantCulture);
        var scale = ReadSuffix(s, ref i);

        for (var j = i; j < s.Length; j++)
        {
            if (!char.IsLetter(s[j]))
            {
                error = new ParseError(offset + j, $"「{text.Trim()}」含有無法辨識的字元「{s[j]}」（第 {offset + j + 1} 個字元）；字尾後只能接單位字母。");
                return false;
            }
        }

        value = number * scale;
        if (!double.IsFinite(value))
        {
            error = new ParseError(offset, $"「{text.Trim()}」超出可表示的數值範圍。");
            return false;
        }

        return true;
    }

    public static double Parse(string text)
    {
        if (!TryParse(text, out var value, out var error))
        {
            throw new FormatException(error.MessageZh);
        }

        return value;
    }

    public static string Format(double value, string? unit = null, int significantDigits = 4)
    {
        unit ??= string.Empty;
        if (value == 0 || !double.IsFinite(value))
        {
            return value.ToString(CultureInfo.InvariantCulture) + unit;
        }

        var exp3 = (int)Math.Floor(Math.Log10(Math.Abs(value)) / 3) * 3;
        exp3 = Math.Clamp(exp3, -15, 12);
        var mantissa = Math.Round(value / Math.Pow(10, exp3), Math.Max(0, significantDigits - 1 - (int)Math.Floor(Math.Log10(Math.Abs(value / Math.Pow(10, exp3))))));
        if (Math.Abs(mantissa) >= 1000 && exp3 < 12)
        {
            exp3 += 3;
            mantissa /= 1000;
        }

        var suffix = FormatSuffixes.First(p => p.Exponent == exp3).Suffix;
        var text = mantissa.ToString("G" + significantDigits, CultureInfo.InvariantCulture);
        return text + suffix + unit;
    }

    private static double ReadSuffix(string s, ref int i)
    {
        if (i >= s.Length)
        {
            return 1;
        }

        if (s.Length - i >= 3 && string.Compare(s, i, "meg", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)
        {
            i += 3;
            return 1e6;
        }

        var scale = char.ToLowerInvariant(s[i]) switch
        {
            'f' => 1e-15,
            'p' => 1e-12,
            'n' => 1e-9,
            'u' or 'µ' or 'μ' => 1e-6,
            'm' => 1e-3,
            'k' => 1e3,
            'g' => 1e9,
            't' => 1e12,
            _ => 0.0,
        };

        if (scale == 0)
        {
            return 1;
        }

        i++;
        return scale;
    }
}
