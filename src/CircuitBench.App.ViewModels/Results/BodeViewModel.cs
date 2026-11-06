using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CircuitBench.Core.Analysis;
using CircuitBench.Core.Bode;

namespace CircuitBench.App.ViewModels.Results;

/// <summary>波德圖：振幅 (dB) 與展開後的相位，對數頻率軸。</summary>
public sealed partial class BodeViewModel : ObservableObject
{
    private ComplexSignalSet? _data;

    public ObservableCollection<SignalId> AvailableSignals { get; } = [];

    [ObservableProperty]
    private SignalId? _selectedSignal;

    public double[] Frequencies => _data?.Frequency ?? [];

    public double[] MagnitudeDb { get; private set; } = [];

    public double[] PhaseDegrees { get; private set; } = [];

    public bool HasData => _data is not null;

    public ComplexSignalSet? Data => _data;

    public event EventHandler? Changed;

    public void Load(ComplexSignalSet data)
    {
        _data = data;
        var previous = SelectedSignal;
        AvailableSignals.Clear();
        foreach (var id in data.Signals.Keys.OrderBy(k => k.Kind).ThenBy(k => k.Name, StringComparer.OrdinalIgnoreCase))
        {
            AvailableSignals.Add(id);
        }

        SelectedSignal = previous is { } p && data.Signals.ContainsKey(p)
            ? p
            : AvailableSignals.FirstOrDefault(s => s.Kind == SignalKind.Voltage);
        Recompute();
        OnPropertyChanged(nameof(HasData));
    }

    partial void OnSelectedSignalChanged(SignalId? value) => Recompute();

    private void Recompute()
    {
        if (_data is null || SelectedSignal is not { } id || !_data.Signals.TryGetValue(id, out var h))
        {
            MagnitudeDb = [];
            PhaseDegrees = [];
        }
        else
        {
            MagnitudeDb = BodeMath.MagnitudeDb(h);
            PhaseDegrees = BodeMath.UnwrappedPhaseDegrees(h);
        }

        OnPropertyChanged(nameof(MagnitudeDb));
        OnPropertyChanged(nameof(PhaseDegrees));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<Tick> FrequencyTicks =>
        Frequencies.Length > 0 ? AxisTicks.Log10(Frequencies[0], Frequencies[^1]) : [];

    public (double Min, double Max) MagnitudeRange => Range(MagnitudeDb, 10);

    public (double Min, double Max) PhaseRange => Range(PhaseDegrees, 45);

    private static (double, double) Range(double[] values, double step)
    {
        var finite = values.Where(double.IsFinite).ToArray();
        if (finite.Length == 0)
        {
            return (-1, 1);
        }

        var min = Math.Floor(finite.Min() / step) * step;
        var max = Math.Ceiling(finite.Max() / step) * step;
        return max > min ? (min, max) : (min - step, max + step);
    }
}
