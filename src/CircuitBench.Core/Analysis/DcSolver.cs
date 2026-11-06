using CircuitBench.Core.Mna;
using CircuitBench.Core.Mna.Devices;

namespace CircuitBench.Core.Analysis;

/// <summary>
/// Newton–Raphson 求解與收斂策略：直接 Newton → gmin 步進 → 電源步進。
/// 收斂條件：每個未知數的變化 &lt; reltol·|值| + 絕對容許值（電壓 vntol、電流 abstol），
/// 且沒有元件套用接面電壓限制。
/// </summary>
internal sealed class DcSolver(MnaSystem system, SimulationOptions options, CancellationToken cancellation)
{
    private string _lastWorst = "（未知）";

    public int Iterations { get; private set; }

    public ConvergenceMethod LastMethod { get; private set; }

    /// <summary>單次 Newton；成功回傳解，失敗回傳 null。</summary>
    public double[]? Newton(double[] initial, double extraGmin, int maxIterations)
    {
        var x = (double[])initial.Clone();
        foreach (var d in system.Devices)
        {
            if (d is DiodeDevice diode)
            {
                diode.ResetJunction(x);
            }
        }

        for (var iter = 0; iter < maxIterations; iter++)
        {
            cancellation.ThrowIfCancellationRequested();
            Iterations++;
            var xNew = system.AssembleAndSolve(x, extraGmin);
            if (xNew.Any(v => !double.IsFinite(v)))
            {
                _lastWorst = "解出現非數值";
                return null;
            }

            if (!system.HasNonlinear)
            {
                return xNew;
            }

            var converged = IsConverged(x, xNew);
            x = xNew;
            if (converged && iter > 0 && !system.Devices.Any(d => d.WasLimited))
            {
                return x;
            }
        }

        return null;
    }

    /// <summary>直流工作點，含收斂輔助策略。</summary>
    public double[] SolveOperatingPoint(double[]? initial = null)
    {
        var x0 = initial ?? new double[system.Size];
        system.Context.SourceScale = 1;

        if (!system.HasNonlinear)
        {
            LastMethod = ConvergenceMethod.Linear;
            return Newton(x0, 0, 1)!;
        }

        if (!options.SkipDirectNewton)
        {
            var direct = Newton(x0, 0, options.MaxNewtonIterations);
            if (direct is not null)
            {
                LastMethod = ConvergenceMethod.Newton;
                return Polish(direct);
            }
        }

        var gmin = GminStepping(x0);
        if (gmin is not null)
        {
            LastMethod = ConvergenceMethod.GminStepping;
            return Polish(gmin);
        }

        var source = SourceStepping();
        if (source is not null)
        {
            LastMethod = ConvergenceMethod.SourceStepping;
            return Polish(source);
        }

        throw new ConvergenceException(
            $"直流工作點不收斂（已嘗試 Newton、gmin 步進與電源步進）。最後未收斂的是：{_lastWorst}。", _lastWorst);
    }

    /// <summary>
    /// 收斂後再做一次 Newton 迭代：二次收斂使誤差遠小於收斂門檻，
    /// 讓直流工作點的精度不受 reltol 限制（只用於工作點，不用於暫態的每一步）。
    /// </summary>
    private double[] Polish(double[] x)
    {
        var polished = system.AssembleAndSolve(x, 0);
        Iterations++;
        return polished.All(double.IsFinite) ? polished : x;
    }

    private double[]? GminStepping(double[] x0)
    {
        var x = x0;
        double? previous = null;
        var g = 1e-2;
        var factor = 10.0;
        while (true)
        {
            var next = Newton(x, g, options.MaxNewtonIterations);
            if (next is null)
            {
                // 退回上一個成功的 gmin，並縮小步進倍率
                factor = Math.Sqrt(factor);
                if (previous is null || factor < 1.05)
                {
                    return null;
                }

                g = previous.Value / factor;
                continue;
            }

            x = next;
            previous = g;
            if (g <= options.Gmin)
            {
                return Newton(x, 0, options.MaxNewtonIterations);
            }

            g = Math.Max(g / factor, options.Gmin);
        }
    }

    private double[]? SourceStepping()
    {
        var x = new double[system.Size];
        var scale = 0.0;
        var step = 0.1;
        try
        {
            system.Context.SourceScale = 0;
            x = Newton(x, 0, options.MaxNewtonIterations) ?? x;
            while (scale < 1)
            {
                var target = Math.Min(1, scale + step);
                system.Context.SourceScale = target;
                var next = Newton(x, 0, options.MaxNewtonIterations);
                if (next is null)
                {
                    step /= 2;
                    if (step < 1e-6)
                    {
                        return null;
                    }

                    continue;
                }

                x = next;
                scale = target;
                step = Math.Min(step * 1.5, 0.25);
            }

            return x;
        }
        finally
        {
            system.Context.SourceScale = 1;
        }
    }

    private bool IsConverged(double[] x, double[] xNew)
    {
        var converged = true;
        var worstRatio = 0.0;
        var worst = -1;
        for (var i = 0; i < x.Length; i++)
        {
            var abs = system.Layout.IsVoltageUnknown(i) ? options.VnTol : options.AbsTol;
            var tol = options.RelTol * Math.Max(Math.Abs(xNew[i]), Math.Abs(x[i])) + abs;
            var ratio = Math.Abs(xNew[i] - x[i]) / tol;
            if (ratio > 1)
            {
                converged = false;
            }

            if (ratio > worstRatio)
            {
                worstRatio = ratio;
                worst = i;
            }
        }

        if (worst >= 0 && !converged)
        {
            _lastWorst = system.Layout.DescribeUnknown(worst);
        }

        return converged;
    }
}
