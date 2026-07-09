namespace Guilred.Rendering;

public enum XAlignment {
    Left, Center, Right
}

public enum YAlignment {
    Top, Center, Bottom
}
public record struct Alignment(XAlignment xAlignment = XAlignment.Left, YAlignment yAlignment = YAlignment.Top, XAlignment textAlignment = XAlignment.Left) {
    public Alignment(YAlignment yAlignment) : this(XAlignment.Left, yAlignment, XAlignment.Left) { }

    public static readonly Alignment Centered = new(XAlignment.Center, YAlignment.Center);
    public static readonly Alignment TopCentered = new(XAlignment.Center, YAlignment.Top);
    public static readonly Alignment BottomCentered = new(XAlignment.Center, YAlignment.Bottom);
    public static readonly Alignment LeftCentered = new(XAlignment.Left, YAlignment.Center);
    public static readonly Alignment RightCentered = new(XAlignment.Right, YAlignment.Center);
}