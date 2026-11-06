using System.Globalization;
using System.Text;
using CircuitBench.Core.Analysis;
using CircuitBench.Core.Bode;

namespace CircuitBench.Core.IO;

/// <summary>波形匯出 CSV：UTF-8 with BOM（Excel 可直接開啟中文），InvariantCulture，數值用 "R" 格式保證可往返。</summary>
public static class CsvExporter
{
    public static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static void Write(Stream stream, RealSignalSet data, IEnumerable<SignalId> signals, string? xHeader = null)
    {
        var ids = signals.ToList();
        using var writer = new StreamWriter(stream, Utf8WithBom, leaveOpen: true) { NewLine = "\r\n" };
        writer.WriteLine(string.Join(",", new[] { Escape(xHeader ?? data.XLabel) }.Concat(ids.Select(i => Escape(i.ToString())))));
        var columns = ids.Select(i => data[i]).ToList();
        for (var r = 0; r < data.X.Length; r++)
        {
            writer.Write(data.X[r].ToString("R", CultureInfo.InvariantCulture));
            foreach (var col in columns)
            {
                writer.Write(',');
                writer.Write(col[r].ToString("R", CultureInfo.InvariantCulture));
            }

            writer.WriteLine();
        }
    }

    /// <summary>交流結果：頻率、每個訊號的 dB 與（展開後的）相位。</summary>
    public static void WriteAc(Stream stream, ComplexSignalSet data, IEnumerable<SignalId> signals)
    {
        var ids = signals.ToList();
        using var writer = new StreamWriter(stream, Utf8WithBom, leaveOpen: true) { NewLine = "\r\n" };
        var header = new List<string> { "頻率 (Hz)" };
        foreach (var id in ids)
        {
            header.Add(Escape($"{id} 振幅 (dB)"));
            header.Add(Escape($"{id} 相位 (度)"));
        }

        writer.WriteLine(string.Join(",", header));
        var mags = ids.Select(i => BodeMath.MagnitudeDb(data[i])).ToList();
        var phases = ids.Select(i => BodeMath.UnwrappedPhaseDegrees(data[i])).ToList();
        for (var r = 0; r < data.Frequency.Length; r++)
        {
            writer.Write(data.Frequency[r].ToString("R", CultureInfo.InvariantCulture));
            for (var c = 0; c < ids.Count; c++)
            {
                writer.Write(',');
                writer.Write(mags[c][r].ToString("R", CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(phases[c][r].ToString("R", CultureInfo.InvariantCulture));
            }

            writer.WriteLine();
        }
    }

    private static string Escape(string s) =>
        s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
