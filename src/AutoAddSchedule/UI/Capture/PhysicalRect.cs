namespace AutoAddSchedule.UI.Capture;

/// <summary>
/// 物理像素矩形。坐标以虚拟屏幕原点为基准，位于主屏左侧/上方的显示器可能出现负坐标。
/// 截图模块内部一律使用物理像素，避免高 DPI 下的缩放误差。
/// </summary>
public readonly struct PhysicalRect : IEquatable<PhysicalRect>
{
    /// <summary>空矩形。</summary>
    public static PhysicalRect Empty => new(0, 0, 0, 0);

    public PhysicalRect(int left, int top, int width, int height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    public int Left { get; }

    public int Top { get; }

    public int Width { get; }

    public int Height { get; }

    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>用任意两个对角点构造矩形（自动归一化，支持反向拖动）。</summary>
    public static PhysicalRect FromCorners(int x1, int y1, int x2, int y2)
    {
        return new PhysicalRect(
            Math.Min(x1, x2),
            Math.Min(y1, y2),
            Math.Abs(x2 - x1),
            Math.Abs(y2 - y1));
    }

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    /// <summary>与另一个矩形求交集，无交集时返回 <see cref="Empty"/>。</summary>
    public PhysicalRect Intersect(PhysicalRect other)
    {
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? Empty : new PhysicalRect(left, top, right - left, bottom - top);
    }

    public bool IntersectsWith(PhysicalRect other) => !Intersect(other).IsEmpty;

    public bool Equals(PhysicalRect other)
        => Left == other.Left && Top == other.Top && Width == other.Width && Height == other.Height;

    public override bool Equals(object? obj) => obj is PhysicalRect other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Left, Top, Width, Height);

    public override string ToString() => "(" + Left + "," + Top + ") " + Width + "×" + Height;
}
