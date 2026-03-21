namespace PanacheUI.Core;

/// <summary>Four-sided spacing: padding, margin, or border width.</summary>
public readonly record struct EdgeSize(float Top, float Right, float Bottom, float Left)
{
    /// <summary>Uniform value on all four sides.</summary>
    public EdgeSize(float all) : this(all, all, all, all) { }

    /// <summary>Vertical (top+bottom) and horizontal (left+right).</summary>
    public EdgeSize(float vertical, float horizontal) : this(vertical, horizontal, vertical, horizontal) { }

    public float Horizontal => Left + Right;
    public float Vertical   => Top  + Bottom;

    public static readonly EdgeSize Zero = new(0);
}
