namespace Guilred.Rendering;

public enum XAlignment {
    Left, Center, Right
}

public enum YAlignment {
    Top, Center, Bottom
}
public record struct Alignment(XAlignment xAlignment = XAlignment.Left, YAlignment yAlignment = YAlignment.Top, XAlignment textAlignment = XAlignment.Right) {
    public Alignment(YAlignment yAlignment) : this(XAlignment.Left, yAlignment, XAlignment.Left) { }

    public static readonly Alignment Centered = new(XAlignment.Center, YAlignment.Center, XAlignment.Center);
    public static readonly Alignment TopCentered = new(XAlignment.Center, YAlignment.Top, XAlignment.Center);
}