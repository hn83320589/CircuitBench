namespace CircuitBench.TestKit;

/// <summary>Lambert W 主分支（x ≥ 0），以 Halley 迭代求解 w·e^w = x。供二極體解析解使用。</summary>
public static class LambertW
{
    public static double W0(double x)
    {
        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (x == 0)
        {
            return 0;
        }

        // 初值：小 x 用 x，大 x 用 ln x − ln ln x
        var w = x < 3 ? Math.Log(1 + x) : Math.Log(x) - Math.Log(Math.Log(x));
        for (var i = 0; i < 100; i++)
        {
            var ew = Math.Exp(w);
            var f = w * ew - x;
            var wNew = w - f / (ew * (w + 1) - (w + 2) * f / (2 * w + 2));
            if (Math.Abs(wNew - w) <= 1e-15 * (1 + Math.Abs(wNew)))
            {
                return wNew;
            }

            w = wNew;
        }

        return w;
    }

    /// <summary>以 ln(x) 給定引數（避免 e^大數 溢位）：解 w + ln w = lnX。</summary>
    public static double W0FromLog(double lnX)
    {
        if (lnX < 500)
        {
            return W0(Math.Exp(lnX));
        }

        var w = lnX - Math.Log(lnX);
        for (var i = 0; i < 100; i++)
        {
            var f = w + Math.Log(w) - lnX;
            var wNew = w - f / (1 + 1 / w);
            if (Math.Abs(wNew - w) <= 1e-15 * w)
            {
                return wNew;
            }

            w = wNew;
        }

        return w;
    }

    /// <summary>串聯電阻 R 與二極體（無 gmin）的電流解析解。</summary>
    public static double SeriesDiodeCurrent(double vs, double r, double isat, double n, double vt = 0.025852)
    {
        var nvt = n * vt;
        var lnArg = Math.Log(isat * r / nvt) + (vs + isat * r) / nvt;
        return nvt / r * W0FromLog(lnArg) - isat;
    }
}
