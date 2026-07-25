namespace Guilred.Rendering;

public enum XAlignment {
    Left, Center, Right
}

public enum YAlignment {
    Top, Center, Bottom
}


public struct Alignment(XAlignment xAlignment = XAlignment.Left, YAlignment yAlignment = YAlignment.Top, XAlignment textAlignment = XAlignment.Left) {
    public XAlignment xAlignment = xAlignment;
    public YAlignment yAlignment = yAlignment;
    public XAlignment textAlignment = textAlignment;
    // NB: xAlign is for X POSITION, textAlign is for text alignment within the box resulted from xAlign
    public Alignment(YAlignment yAlignment) : this(XAlignment.Left, yAlignment, XAlignment.Left) { }

    public static readonly Alignment TopLeft = new(XAlignment.Left, YAlignment.Top);
    public static readonly Alignment Centered = new(XAlignment.Center, YAlignment.Center, XAlignment.Center);
    public static readonly Alignment TopCentered = new(XAlignment.Center, YAlignment.Top);
    public static readonly Alignment BottomCentered = new(XAlignment.Center, YAlignment.Bottom);
    public static readonly Alignment LeftCentered = new(XAlignment.Left, YAlignment.Center);
    public static readonly Alignment RightCentered = new(XAlignment.Right, YAlignment.Center);
}