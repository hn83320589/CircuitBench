using CircuitBench.Core.Mna;
using CircuitBench.Core.Mna.Devices;

namespace CircuitBench.Core.Analysis;

/// <summary>
/// 暫態分析（演算法說明另見 README）。
/// <list type="bullet">
/// <item>起點：以波形在 t=0 的值求直流工作點。</item>
/// <item>積分：t=0 與每個轉折點之後，先走兩步後向尤拉（BE），再用 BDF2
/// （L-stable，剛性模態被衰減而不會正負交替）直到步長穩定，之後改用梯形法（TR，LC 能量守恆）。
/// 若在 TR 期間偵測到連續正負交替，也會切回 BDF2。</item>
/// <item>步長：以狀態量（電容電壓、電感電流）的除差估計局部截斷誤差；
/// 誤差比 &gt; 1 則拒絕並縮小步長。被拒絕的步驟不更新元件歷史，也不寫入結果。</item>
/// <item>轉折點：電源轉折與開關切換時刻收集成佇列。步長會被截短，並直接把時間設成轉折點本身，
/// 因此時間誤差為 0。落在轉折點上的那一步以左極限（切換前）求值，下一步才使用切換後的狀態。</item>
/// </list>
/// </summary>
internal sealed class TransientEngine(
    MnaSystem system, TransientRequest request, SimulationOptions options, MonotonicProgress progress, CancellationToken cancellation)
{
    private const int BackwardEulerSteps = 2;

    private enum Phase
    {
        BackwardEuler,
        Bdf2,
        Trapezoidal,
    }

    private readonly List<double> _times = [];
    private readonly List<(double Time, double[] States)> _history = [];
    private readonly List<StateQuantity> _stateBuffer = [];
    private bool[] _isVoltage = [];

    public (RealSignalSet Data, int Iterations, int Accepted, int Rejected, ConvergenceMethod Method) Run()
    {
        var stop = request.StopTime;
        var maxStep = request.EffectiveMaxStep;
        if (!(stop > 0) || !double.IsFinite(stop))
        {
            throw new SimulationException("暫態分析的停止時間必須大於 0。");
        }

        if (!(maxStep > 0))
        {
            throw new SimulationException("暫態分析的最大步長必須大於 0。");
        }

        var ctx = system.Context;
        var solver = new DcSolver(system, options, cancellation);

        // 1. 起點工作點（電源取 t=0 的波形值）
        ctx.Mode = LoadMode.Dc;
        ctx.UseWaveformsInDc = true;
        ctx.Time = 0;
        double[] x;
        try
        {
            x = solver.SolveOperatingPoint();
        }
        finally
        {
            ctx.UseWaveformsInDc = false;
        }

        var opMethod = solver.LastMethod;
        foreach (var d in system.Devices)
        {
            d.InitializeTransient(x);
        }

        // 2. 轉折點佇列（含停止時間）
        var breakpoints = system.Devices
            .SelectMany(d => d.Breakpoints(0, stop))
            .Where(t => t > 0 && t < stop)
            .Append(stop)
            .Distinct()
            .Order()
            .ToArray();
        var nextBp = 0;

        var recorder = new Recorder(system, request);
        recorder.Record(x);
        _times.Add(0);

        ctx.Mode = LoadMode.Transient;
        var t = 0.0;
        var h = Math.Min(maxStep, breakpoints[0]) * 0.01;
        var phase = Phase.BackwardEuler;
        var phaseSteps = 0;
        var accepted = 0;
        var rejected = 0;
        var minStep = stop * 1e-14;

        while (t < stop)
        {
            cancellation.ThrowIfCancellationRequested();
            while (breakpoints[nextBp] <= t)
            {
                nextBp++;
            }

            var bp = breakpoints[nextBp];
            h = Math.Min(h, maxStep);
            double tNew;
            var landsOnBreakpoint = false;
            if (t + h >= bp)
            {
                tNew = bp;
                landsOnBreakpoint = true;
            }
            else
            {
                if (bp - (t + h) < 0.25 * h)
                {
                    h = (bp - t) / 2;
                }

                tNew = t + h;
            }

            var hActual = tNew - t;
            if (hActual < minStep || tNew <= t)
            {
                throw new SimulationException($"暫態分析在 t = {t:G6} s 附近的時間步長過小，無法繼續（可能是電路變化太劇烈）。");
            }

            var method = phase switch
            {
                Phase.BackwardEuler => IntegrationMethod.BackwardEuler,
                Phase.Bdf2 => IntegrationMethod.Bdf2,
                _ => IntegrationMethod.Trapezoidal,
            };
            ctx.Method = method;
            ctx.TimeStep = hActual;
            ctx.Coefficients = method switch
            {
                IntegrationMethod.BackwardEuler => IntegrationCoefficients.BackwardEuler(hActual),
                IntegrationMethod.Bdf2 => IntegrationCoefficients.Bdf2(hActual, t - _times[^2]),
                _ => IntegrationCoefficients.Trapezoidal(hActual),
            };

            // 落在轉折點的步驟以左極限求值（切換前的狀態）
            ctx.Time = landsOnBreakpoint ? Math.BitDecrement(tNew) : tNew;

            var xNew = solver.Newton(x, 0, options.MaxNewtonIterations);
            if (xNew is null)
            {
                rejected++;
                h = hActual / 8;
                continue;
            }

            var ratio = ErrorRatio(tNew, xNew, method);
            var order = method == IntegrationMethod.BackwardEuler ? 1 : 2;
            if (ratio > 1)
            {
                rejected++;
                h = hActual * Math.Max(0.25, 0.9 * Math.Pow(ratio, -1.0 / (order + 1)));
                continue;
            }

            // ---- 接受 ----
            foreach (var d in system.Devices)
            {
                d.AcceptTransient(xNew, ctx);
            }

            x = xNew;
            t = tNew;
            accepted++;
            if (_times.Count >= options.MaxTimePoints)
            {
                throw new SimulationException($"暫態結果超過 {options.MaxTimePoints:N0} 個時間點的上限，請加大最大步長或縮短停止時間。");
            }

            _times.Add(t);
            recorder.Record(x);

            var grow = ratio < 0 ? 1 : ratio == 0 ? 2 : Math.Min(2, 0.9 * Math.Pow(ratio, -1.0 / (order + 1)));
            var hNext = hActual * Math.Max(grow, 0.25);

            if (landsOnBreakpoint)
            {
                // 轉折點：歷史從下一個點重新開始，先走 BE
                _history.Clear();
                phase = Phase.BackwardEuler;
                phaseSteps = 0;
                var following = nextBp + 1 < breakpoints.Length ? breakpoints[nextBp + 1] - t : maxStep;
                h = 0.1 * Math.Min(Math.Max(hNext, hActual), following);
            }
            else
            {
                PushHistory(t, xNew);
                phaseSteps++;
                h = hNext;
                switch (phase)
                {
                    case Phase.BackwardEuler when phaseSteps >= BackwardEulerSteps:
                        phase = Phase.Bdf2;
                        phaseSteps = 0;
                        break;
                    case Phase.Bdf2 when phaseSteps >= 3 && (hNext <= 1.25 * hActual || hActual >= 0.999 * maxStep):
                        phase = Phase.Trapezoidal;
                        phaseSteps = 0;
                        break;
                    case Phase.Trapezoidal when IsRinging():
                        phase = Phase.Bdf2;
                        phaseSteps = 0;
                        break;
                }
            }

            progress.Report(t / stop);
        }

        progress.Complete();
        return (recorder.Build(_times), solver.Iterations, accepted, rejected, opMethod);
    }

    private double[] CollectStates(double[] x)
    {
        _stateBuffer.Clear();
        foreach (var d in system.Devices)
        {
            d.CollectStates(x, _stateBuffer);
        }

        if (_isVoltage.Length != _stateBuffer.Count)
        {
            _isVoltage = _stateBuffer.Select(s => s.IsVoltage).ToArray();
        }

        var values = new double[_stateBuffer.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = _stateBuffer[i].Value;
        }

        return values;
    }

    private void PushHistory(double t, double[] x)
    {
        _history.Add((t, CollectStates(x)));
        if (_history.Count > 4)
        {
            _history.RemoveAt(0);
        }
    }

    private double Tolerance(int i, double magnitude) =>
        options.TrTol * (options.RelTol * magnitude + (_isVoltage[i] ? options.VnTol : options.AbsTol));

    private double LocalScale(int i, double current)
    {
        var m = Math.Abs(current);
        foreach (var (_, s) in _history)
        {
            m = Math.Max(m, Math.Abs(s[i]));
        }

        return m;
    }

    /// <summary>估計 LTE 與容許值的比值；歷史不足時回傳 −1（接受但不放大步長）。</summary>
    private double ErrorRatio(double tNew, double[] xNew, IntegrationMethod method)
    {
        var needed = method == IntegrationMethod.BackwardEuler ? 2 : 3;
        if (_history.Count < needed)
        {
            return -1;
        }

        var states = CollectStates(xNew);
        if (states.Length == 0)
        {
            return 0;
        }

        var h = tNew - _history[^1].Time;
        var worst = 0.0;
        for (var i = 0; i < states.Length; i++)
        {
            double lte;
            if (method == IntegrationMethod.BackwardEuler)
            {
                var (t1, s1) = _history[^2];
                var (t2, s2) = _history[^1];
                lte = h * h * DividedDifference2(t1, s1[i], t2, s2[i], tNew, states[i]);
            }
            else
            {
                var (t0, s0) = _history[^3];
                var (t1, s1) = _history[^2];
                var (t2, s2) = _history[^1];
                var dd3 = DividedDifference3(t0, s0[i], t1, s1[i], t2, s2[i], tNew, states[i]);

                // x''' ≈ 6·DD3；TR 的 LTE = h³x'''/12，BDF2 的 LTE ≈ 2h³x'''/9
                lte = method == IntegrationMethod.Trapezoidal ? h * h * h * dd3 / 2 : 4.0 / 3 * h * h * h * dd3;
            }

            worst = Math.Max(worst, Math.Abs(lte) / Tolerance(i, LocalScale(i, states[i])));
        }

        return worst;
    }

    /// <summary>最近四個點的某個狀態量出現連續兩次正負交替（且幅度超過容許值）。</summary>
    private bool IsRinging()
    {
        if (_history.Count < 4)
        {
            return false;
        }

        for (var i = 0; i < _history[^1].States.Length; i++)
        {
            var d1 = _history[^3].States[i] - _history[^4].States[i];
            var d2 = _history[^2].States[i] - _history[^3].States[i];
            var d3 = _history[^1].States[i] - _history[^2].States[i];
            var tol = Tolerance(i, LocalScale(i, _history[^1].States[i])) / options.TrTol;
            if (d1 * d2 < 0 && d2 * d3 < 0 && Math.Abs(d2) > tol && Math.Abs(d3) > tol)
            {
                return true;
            }
        }

        return false;
    }

    private static double DividedDifference2(double t0, double y0, double t1, double y1, double t2, double y2)
    {
        var d01 = (y1 - y0) / (t1 - t0);
        var d12 = (y2 - y1) / (t2 - t1);
        return (d12 - d01) / (t2 - t0);
    }

    private static double DividedDifference3(double t0, double y0, double t1, double y1, double t2, double y2, double t3, double y3)
    {
        var a = DividedDifference2(t0, y0, t1, y1, t2, y2);
        var b = DividedDifference2(t1, y1, t2, y2, t3, y3);
        return (b - a) / (t3 - t0);
    }

    /// <summary>逐步記錄選定的訊號；分析完成後才組成不可變結果。</summary>
    private sealed class Recorder
    {
        private readonly MnaSystem _system;
        private readonly List<(SignalId Id, int Unknown, Device? Device)> _channels = [];
        private readonly List<List<double>> _columns = [];

        public Recorder(MnaSystem system, TransientRequest request)
        {
            _system = system;
            var wanted = request.SavedSignals is null ? null : new HashSet<SignalId>(request.SavedSignals);
            for (var i = 0; i < system.Layout.NodeCount; i++)
            {
                var id = SignalId.V(system.Layout.NodeNames[i].Value);
                if (wanted is null || wanted.Contains(id))
                {
                    _channels.Add((id, i, null));
                }
            }

            foreach (var d in system.Devices)
            {
                var id = SignalId.I(d.Name);
                if (wanted is null || wanted.Contains(id))
                {
                    _channels.Add((id, -1, d));
                }
            }

            foreach (var _ in _channels)
            {
                _columns.Add([]);
            }
        }

        public void Record(double[] x)
        {
            for (var c = 0; c < _channels.Count; c++)
            {
                var (_, unknown, device) = _channels[c];
                _columns[c].Add(device is null ? x[unknown] : device.Current(x, _system.Context));
            }
        }

        public RealSignalSet Build(List<double> times)
        {
            var signals = new Dictionary<SignalId, double[]>();
            for (var c = 0; c < _channels.Count; c++)
            {
                signals[_channels[c].Id] = [.. _columns[c]];
            }

            return new RealSignalSet([.. times], signals, "時間 (s)");
        }
    }
}
