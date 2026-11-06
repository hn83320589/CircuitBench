using System.Numerics;
using CircuitBench.Core.LinearAlgebra;

namespace CircuitBench.Core.Mna.Devices;

/// <summary>
/// 元件的模擬行為。結構（非零位置）只在 <see cref="Register"/> 登記一次，
/// 直流、暫態與交流共用同一個結構，因此符號分解可以重用。
/// </summary>
public abstract class Device(string name)
{
    public string Name { get; } = name;

    public virtual bool IsNonlinear => false;

    /// <summary>上一次 Load 是否套用了接面電壓限制（有限制就不能宣告收斂）。</summary>
    public virtual bool WasLimited => false;

    public abstract void Register(PatternBuilder pattern);

    public abstract void Bind(SparsityPattern pattern);

    public abstract void Load(RealLoadContext ctx);

    public abstract void LoadAc(ComplexLoadContext ctx);

    /// <summary>以直流工作點初始化暫態歷史。</summary>
    public virtual void InitializeTransient(double[] x)
    {
    }

    /// <summary>只有被接受的時間步才會呼叫，用來更新歷史狀態。</summary>
    public virtual void AcceptTransient(double[] x, RealLoadContext ctx)
    {
    }

    /// <summary>元件自身的轉折時刻（電源波形、開關切換）。</summary>
    public virtual IEnumerable<double> Breakpoints(double tStart, double tStop) => [];

    /// <summary>
    /// 支路電流（兩端元件：由第一個端點經元件流向第二個端點為正）。
    /// 暫態中電容電流取自上次接受的步驟。
    /// </summary>
    public abstract double Current(double[] x, RealLoadContext ctx);

    /// <summary>暫態局部截斷誤差估計使用的狀態量（電容電壓、電感電流）。</summary>
    public virtual void CollectStates(double[] x, List<StateQuantity> states)
    {
    }
}

public readonly record struct StateQuantity(double Value, bool IsVoltage);

/// <summary>
/// 記錄每個矩陣元素「抵消前」的量級：組裝時所有蓋印值絕對值的最大者。
/// 奇異判定以它為尺度，才能偵測同一元素內的抵消（例如受控源的負電導剛好抵消電阻）。
/// </summary>
internal static class StampScale
{
    public static void Track(double[] scale, int slot, double magnitude)
    {
        if (slot >= 0 && magnitude > scale[slot])
        {
            scale[slot] = magnitude;
        }
    }

    public static double Magnitude<T>(T v)
        where T : INumberBase<T> =>
        typeof(T) == typeof(double) ? Math.Abs((double)(object)v) : ((Complex)(object)v).Magnitude;
}

/// <summary>兩端點之間的導納蓋印（含接地處理）。</summary>
internal struct TwoTerminalSlots
{
    public int P;
    public int Q;
    private int _pp;
    private int _qq;
    private int _pq;
    private int _qp;

    public TwoTerminalSlots(int p, int q)
    {
        P = p;
        Q = q;
        _pp = _qq = _pq = _qp = -1;
    }

    public readonly void Register(PatternBuilder b)
    {
        if (P >= 0) b.Add(P, P);
        if (Q >= 0) b.Add(Q, Q);
        if (P >= 0 && Q >= 0)
        {
            b.Add(P, Q);
            b.Add(Q, P);
        }
    }

    public void Bind(SparsityPattern s)
    {
        _pp = P >= 0 ? s.IndexOf(P, P) : -1;
        _qq = Q >= 0 ? s.IndexOf(Q, Q) : -1;
        _pq = P >= 0 && Q >= 0 ? s.IndexOf(P, Q) : -1;
        _qp = P >= 0 && Q >= 0 ? s.IndexOf(Q, P) : -1;
    }

    public readonly void Stamp<T>(T[] m, T g, double[]? scale = null)
        where T : INumberBase<T>
    {
        if (_pp >= 0) m[_pp] += g;
        if (_qq >= 0) m[_qq] += g;
        if (_pq >= 0) m[_pq] -= g;
        if (_qp >= 0) m[_qp] -= g;
        if (scale is not null)
        {
            var mag = StampScale.Magnitude(g);
            StampScale.Track(scale, _pp, mag);
            StampScale.Track(scale, _qq, mag);
            StampScale.Track(scale, _pq, mag);
            StampScale.Track(scale, _qp, mag);
        }
    }

    /// <summary>由 P 經元件流向 Q 的等效電流源 i（KCL：P 流出、Q 流入）。</summary>
    public readonly void StampCurrent<T>(T[] rhs, T i)
        where T : INumberBase<T>
    {
        if (P >= 0) rhs[P] -= i;
        if (Q >= 0) rhs[Q] += i;
    }

    public readonly double Voltage(double[] x) => (P >= 0 ? x[P] : 0) - (Q >= 0 ? x[Q] : 0);
}

/// <summary>支路電流未知數 k 與兩端點的關聯蓋印（電壓源、受控電壓源、電感）。</summary>
internal struct BranchSlots
{
    public int P;
    public int Q;
    public int K;
    private int _pk;
    private int _kp;
    private int _qk;
    private int _kq;
    private int _kk;

    public BranchSlots(int p, int q, int k)
    {
        P = p;
        Q = q;
        K = k;
        _pk = _kp = _qk = _kq = _kk = -1;
    }

    public readonly void Register(PatternBuilder b, bool withDiagonal)
    {
        if (P >= 0)
        {
            b.Add(P, K);
            b.Add(K, P);
        }

        if (Q >= 0)
        {
            b.Add(Q, K);
            b.Add(K, Q);
        }

        if (withDiagonal)
        {
            b.Add(K, K);
        }
    }

    public void Bind(SparsityPattern s, bool withDiagonal)
    {
        _pk = P >= 0 ? s.IndexOf(P, K) : -1;
        _kp = P >= 0 ? s.IndexOf(K, P) : -1;
        _qk = Q >= 0 ? s.IndexOf(Q, K) : -1;
        _kq = Q >= 0 ? s.IndexOf(K, Q) : -1;
        _kk = withDiagonal ? s.IndexOf(K, K) : -1;
    }

    public readonly void StampIncidence<T>(T[] m, double[]? scale = null)
        where T : INumberBase<T>
    {
        if (_pk >= 0)
        {
            m[_pk] += T.One;
            m[_kp] += T.One;
        }

        if (_qk >= 0)
        {
            m[_qk] -= T.One;
            m[_kq] -= T.One;
        }

        if (scale is not null)
        {
            StampScale.Track(scale, _pk, 1);
            StampScale.Track(scale, _kp, 1);
            StampScale.Track(scale, _qk, 1);
            StampScale.Track(scale, _kq, 1);
        }
    }

    public readonly void StampDiagonal<T>(T[] m, T value, double[]? scale = null)
        where T : INumberBase<T>
    {
        m[_kk] += value;
        if (scale is not null)
        {
            StampScale.Track(scale, _kk, StampScale.Magnitude(value));
        }
    }

    public readonly double Voltage(double[] x) => (P >= 0 ? x[P] : 0) - (Q >= 0 ? x[Q] : 0);
}
