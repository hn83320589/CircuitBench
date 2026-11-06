namespace CircuitBench.Schematic.Geometry;

/// <summary>格點座標（整數）。圖面所有連線判定都在格點上進行。</summary>
public readonly record struct GridPoint(int X, int Y)
{
    public static GridPoint operator +(GridPoint a, GridPoint b) => new(a.X + b.X, a.Y + b.Y);

    public static GridPoint operator -(GridPoint a, GridPoint b) => new(a.X - b.X, a.Y - b.Y);

    public WorldPoint ToWorld() => new(X, Y);

    public override string ToString() => $"({X},{Y})";
}

/// <summary>世界座標（以格距為單位的實數），用於點選與繪製。</summary>
public readonly record struct WorldPoint(double X, double Y)
{
    public GridPoint Snap() => new((int)Math.Round(X), (int)Math.Round(Y));

    public double DistanceTo(WorldPoint o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));
}

public readonly record struct WorldRect(double Left, double Top, double Right, double Bottom)
{
    public static WorldRect FromPoints(WorldPoint a, WorldPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public double Width => Right - Left;

    public double Height => Bottom - Top;

    public bool Contains(WorldPoint p) => p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;

    public bool Contains(WorldRect r) => r.Left >= Left && r.Right <= Right && r.Top >= Top && r.Bottom <= Bottom;

    public bool Intersects(WorldRect r) => r.Left <= Right && r.Right >= Left && r.Top <= Bottom && r.Bottom >= Top;

    public WorldRect Inflate(double d) => new(Left - d, Top - d, Right + d, Bottom + d);

    public WorldRect Union(WorldRect r) => new(Math.Min(Left, r.Left), Math.Min(Top, r.Top), Math.Max(Right, r.Right), Math.Max(Bottom, r.Bottom));
}

/// <summary>順時針旋轉（螢幕座標 y 向下）。</summary>
public enum Rotation
{
    R0,
    R90,
    R180,
    R270,
}

/// <summary>元件方向：先鏡射（左右翻轉 x → −x）再旋轉。</summary>
public readonly record struct Orientation(Rotation Rotation, bool Mirrored)
{
    public static Orientation Default => new(Rotation.R0, false);

    public GridPoint Apply(GridPoint p)
    {
        var x = Mirrored ? -p.X : p.X;
        var y = p.Y;
        return Rotation switch
        {
            Rotation.R0 => new GridPoint(x, y),
            Rotation.R90 => new GridPoint(-y, x),
            Rotation.R180 => new GridPoint(-x, -y),
            _ => new GridPoint(y, -x),
        };
    }

    public WorldPoint Apply(WorldPoint p)
    {
        var x = Mirrored ? -p.X : p.X;
        var y = p.Y;
        return Rotation switch
        {
            Rotation.R0 => new WorldPoint(x, y),
            Rotation.R90 => new WorldPoint(-y, x),
            Rotation.R180 => new WorldPoint(-x, -y),
            _ => new WorldPoint(y, -x),
        };
    }

    /// <summary>再順時針旋轉 90°。</summary>
    public Orientation RotateClockwise() => this with { Rotation = (Rotation)(((int)Rotation + 1) % 4) };

    /// <summary>在目前方向上左右鏡射（以世界座標的垂直軸）。</summary>
    public Orientation MirrorHorizontally() =>
        // 世界座標的 x → −x 等同於：鏡射旗標反轉，且旋轉角度取負
        new((Rotation)((4 - (int)Rotation) % 4), !Mirrored);
}

public static class GridMath
{
    /// <summary>點是否在水平或垂直線段的「內部」（不含端點）。</summary>
    public static bool IsStrictlyInside(GridPoint p, GridPoint a, GridPoint b)
    {
        if (a.X == b.X)
        {
            return p.X == a.X && p.Y > Math.Min(a.Y, b.Y) && p.Y < Math.Max(a.Y, b.Y);
        }

        if (a.Y == b.Y)
        {
            return p.Y == a.Y && p.X > Math.Min(a.X, b.X) && p.X < Math.Max(a.X, b.X);
        }

        return false;
    }

    public static double DistanceToSegment(WorldPoint p, WorldPoint a, WorldPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 == 0)
        {
            return p.DistanceTo(a);
        }

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return p.DistanceTo(new WorldPoint(a.X + t * dx, a.Y + t * dy));
    }
}
