using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CircuitBench.Core.Analysis;
using CircuitBench.Core.Bode;
using CircuitBench.Core.Measurement;
using CircuitBench.Core.Sampling;
using CircuitBench.Core.Units;

namespace CircuitBench.App.ViewModels.Results;

/// <summary>一條曲線：訊號、顏色、所屬 Y 軸（電壓與電流各自一軸）與抽樣金字塔。</summary>
public sealed class Trace
{
    public Trace(SignalId id, double[] x, double[] y, int colorIndex)
    {
        Id = id;
        X = x;
        Y = y;
        ColorIndex = colorIndex;
        Pyramid = new MinMaxPyramid(x, y);
    }

    public SignalId Id { get; }

    public double[] X { get; }

    public double[] Y { get; }

    public int ColorIndex { get; }

    public MinMaxPyramid Pyramid { get; }

    public string Unit => Id.Kind == SignalKind.Voltage ? "V" : "A";

    public string Label => Id.ToString();
}

/// <summary>Y 軸：每種單位一軸，可各自縮放。</summary>
public sealed class YAxis(string unit)
{
    public string Unit { get; } = unit;

    public double Min { get; set; } = -1;

    public double Max { get; set; } = 1;

    public IReadOnlyList<Tick> Ticks(int maxTicks = 8) => AxisTicks.Linear(Min, Max, maxTicks, Unit);
}

public sealed record CursorReadout(
    double TimeA, double TimeB, double DeltaT, double? InverseDeltaT,
    double ValueA, double ValueB, double DeltaValue, MeasurementResult Measurement, string Unit);

/// <summary>
/// 波形檢視器：多條曲線、多個 Y 軸、縮放平移、自動縮放、兩條游標與量測。
/// 繪製時以「每個像素欄的最小值與最大值」抽樣，任何縮放程度都不遺漏尖峰。
/// </summary>
public sealed partial class WaveformViewModel : ObservableObject
{
    private RealSignalSet? _data;

    public ObservableCollection<SignalId> AvailableSignals { get; } = [];

    public ObservableCollection<Trace> Traces { get; } = [];

    /// <summary>訊號清單（勾選即顯示）。</summary>
    public ObservableCollection<Analysis.SignalToggle> SignalToggles { get; } = [];

    public Dictionary<string, YAxis> Axes { get; } = [];

    [ObservableProperty]
    private double _xMin;

    [ObservableProperty]
    private double _xMax = 1;

    [ObservableProperty]
    private double _cursorA;

    [ObservableProperty]
    private double _cursorB;

    [ObservableProperty]
    private SignalId? _measuredSignal;

    [ObservableProperty]
    private string _xLabel = "時間 (s)";

    public bool HasData => _data is not null;

    public RealSignalSet? Data => _data;

    public event EventHandler? Changed;

    public double DataXMin => _data is { X.Length: > 0 } d ? d.X[0] : 0;

    public double DataXMax => _data is { X.Length: > 0 } d ? d.X[^1] : 1;

    /// <summary>載入新結果；保留仍然存在的已選曲線，否則預設顯示第一個節點電壓。</summary>
    public void Load(RealSignalSet data, IEnumerable<SignalId>? preferred = null)
    {
        var previous = Traces.Select(t => t.Id).ToList();
        _data = data;
        XLabel = data.XLabel;
        AvailableSignals.Clear();
        foreach (var id in data.Signals.Keys.OrderBy(k => k.Kind).ThenBy(k => k.Name, StringComparer.OrdinalIgnoreCase))
        {
            AvailableSignals.Add(id);
        }

        Traces.Clear();
        Axes.Clear();
        var wanted = (preferred ?? previous).Where(data.Signals.ContainsKey).ToList();
        if (wanted.Count == 0)
        {
            // 優先顯示使用者命名的節點（網路標籤），其次才是自動命名的 N$…
            var voltages = AvailableSignals.Where(s => s.Kind == SignalKind.Voltage).ToList();
            var named = voltages.Where(s => !s.Name.StartsWith(Schematic.Connectivity.ConnectivityExtractor.AutoNetPrefix, StringComparison.Ordinal)).ToList();
            var pick = named.Count > 0 ? named : voltages;
            if (pick.Count > 0)
            {
                wanted.Add(pick[0]);
            }
        }

        foreach (var id in wanted)
        {
            AddTraceInternal(id);
        }

        XMin = DataXMin;
        XMax = DataXMax > DataXMin ? DataXMax : DataXMin + 1;
        CursorA = XMin + (XMax - XMin) * 0.25;
        CursorB = XMin + (XMax - XMin) * 0.75;
        MeasuredSignal = Traces.FirstOrDefault()?.Id;
        RebuildToggles();
        AutoScale();
        OnPropertyChanged(nameof(HasData));
    }

    private void SyncToggles()
    {
        foreach (var t in SignalToggles)
        {
            t.SetShownSilently(Traces.Any(x => x.Id.Equals(t.Id)));
        }
    }

    private void RebuildToggles()
    {
        SignalToggles.Clear();
        foreach (var id in AvailableSignals)
        {
            SignalToggles.Add(new Analysis.SignalToggle(id, Traces.Any(t => t.Id.Equals(id)), (s, shown) =>
            {
                if (shown)
                {
                    AddTrace(s);
                }
                else
                {
                    RemoveTrace(s);
                }
            }));
        }
    }

    public void Clear()
    {
        _data = null;
        Traces.Clear();
        Axes.Clear();
        AvailableSignals.Clear();
        SignalToggles.Clear();
        OnPropertyChanged(nameof(HasData));
        Raise();
    }

    public void AddTrace(SignalId id)
    {
        if (_data is null || !_data.Signals.ContainsKey(id) || Traces.Any(t => t.Id.Equals(id)))
        {
            return;
        }

        AddTraceInternal(id);
        MeasuredSignal ??= id;
        SyncToggles();
        AutoScaleY();
        Raise();
    }

    public void RemoveTrace(SignalId id)
    {
        var t = Traces.FirstOrDefault(x => x.Id.Equals(id));
        if (t is null)
        {
            return;
        }

        Traces.Remove(t);
        if (!Traces.Any(x => x.Unit == t.Unit))
        {
            Axes.Remove(t.Unit);
        }

        if (MeasuredSignal.Equals(id))
        {
            MeasuredSignal = Traces.FirstOrDefault()?.Id;
        }

        SyncToggles();
        Raise();
    }

    private void AddTraceInternal(SignalId id)
    {
        var trace = new Trace(id, _data!.X, _data[id], Traces.Count);
        Traces.Add(trace);
        if (!Axes.ContainsKey(trace.Unit))
        {
            Axes[trace.Unit] = new YAxis(trace.Unit);
        }
    }

    [RelayCommand]
    public void AutoScale()
    {
        XMin = DataXMin;
        XMax = DataXMax > DataXMin ? DataXMax : DataXMin + 1;
        AutoScaleY();
        Raise();
    }

    /// <summary>每個 Y 軸依目前 X 範圍內可見資料的最小/最大值調整（上下各留 5%）。</summary>
    public void AutoScaleY()
    {
        foreach (var axis in Axes.Values)
        {
            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            foreach (var t in Traces.Where(t => t.Unit == axis.Unit))
            {
                var lo = t.Pyramid.LowerBound(XMin);
                var hi = t.Pyramid.UpperBound(XMax);
                if (hi > lo)
                {
                    var (mn, mx) = t.Pyramid.RangeMinMax(lo, hi);
                    min = Math.Min(min, mn);
                    max = Math.Max(max, mx);
                }
            }

            if (!double.IsFinite(min))
            {
                (min, max) = (-1, 1);
            }

            if (max - min < 1e-15 * Math.Max(1, Math.Abs(max)))
            {
                var pad = Math.Max(Math.Abs(max) * 0.1, 1e-12);
                min -= pad;
                max += pad;
            }

            var margin = (max - min) * 0.05;
            axis.Min = min - margin;
            axis.Max = max + margin;
        }
    }

    /// <summary>以 X 軸上某點為中心縮放（factor &lt; 1 放大）。</summary>
    public void ZoomX(double anchor, double factor)
    {
        var newMin = anchor - (anchor - XMin) * factor;
        var newMax = anchor + (XMax - anchor) * factor;
        if (newMax - newMin < 1e-18)
        {
            return;
        }

        XMin = newMin;
        XMax = newMax;
        Raise();
    }

    public void ZoomY(string unit, double anchor, double factor)
    {
        if (Axes.TryGetValue(unit, out var a))
        {
            a.Min = anchor - (anchor - a.Min) * factor;
            a.Max = anchor + (a.Max - anchor) * factor;
            Raise();
        }
    }

    public void PanX(double delta)
    {
        XMin += delta;
        XMax += delta;
        Raise();
    }

    public void PanY(string unit, double delta)
    {
        if (Axes.TryGetValue(unit, out var a))
        {
            a.Min += delta;
            a.Max += delta;
            Raise();
        }
    }

    /// <summary>給繪圖控制項：某條曲線在目前範圍、指定像素寬度下的抽樣欄。</summary>
    public MinMaxColumn[] Columns(Trace trace, int pixelWidth) => trace.Pyramid.Decimate(XMin, XMax, pixelWidth);

    public IReadOnlyList<Tick> XTicks(int maxTicks = 10) => AxisTicks.Linear(XMin, XMax, maxTicks, "s");

    /// <summary>游標讀值與 [A, B] 區間量測（以內插計算）。</summary>
    public CursorReadout? Readout
    {
        get
        {
            if (_data is null || MeasuredSignal is not { } id || !_data.Signals.TryGetValue(id, out var y))
            {
                return null;
            }

            var a = Math.Min(CursorA, CursorB);
            var b = Math.Max(CursorA, CursorB);
            var va = Interpolation.At(_data.X, y, CursorA);
            var vb = Interpolation.At(_data.X, y, CursorB);
            var dt = CursorB - CursorA;
            return new CursorReadout(CursorA, CursorB, dt, dt == 0 ? null : 1 / dt, va, vb, vb - va,
                Measurements.Measure(_data.X, y, a, b), id.Kind == SignalKind.Voltage ? "V" : "A");
        }
    }

    public string ReadoutText
    {
        get
        {
            if (Readout is not { } r)
            {
                return "沒有資料";
            }

            var m = r.Measurement;
            var f = m.Frequency is { } freq ? EngineeringNotation.Format(freq, "Hz") : "—";
            var inv = r.InverseDeltaT is { } i ? EngineeringNotation.Format(i, "Hz") : "—";
            return $"A: {EngineeringNotation.Format(r.TimeA, "s")} = {EngineeringNotation.Format(r.ValueA, r.Unit)}　" +
                   $"B: {EngineeringNotation.Format(r.TimeB, "s")} = {EngineeringNotation.Format(r.ValueB, r.Unit)}\n" +
                   $"Δt = {EngineeringNotation.Format(r.DeltaT, "s")}　1/Δt = {inv}　Δ = {EngineeringNotation.Format(r.DeltaValue, r.Unit)}\n" +
                   $"平均 {EngineeringNotation.Format(m.Average, r.Unit)}　RMS {EngineeringNotation.Format(m.Rms, r.Unit)}　" +
                   $"峰對峰 {EngineeringNotation.Format(m.PeakToPeak, r.Unit)}　頻率 {f}";
        }
    }

    partial void OnCursorAChanged(double value) => RaiseReadout();

    partial void OnCursorBChanged(double value) => RaiseReadout();

    partial void OnMeasuredSignalChanged(SignalId? value) => RaiseReadout();

    private void RaiseReadout()
    {
        OnPropertyChanged(nameof(Readout));
        OnPropertyChanged(nameof(ReadoutText));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Raise()
    {
        RaiseReadout();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
