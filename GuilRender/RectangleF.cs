using System;
using Microsoft.Xna.Framework;

namespace Guilred.Shapes;

public enum Anchor {
    Top,
    Right,
    Bottom,
    Left,
    Center,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public struct RectangleF(float x, float y, float width, float height) : IEquatable<RectangleF> {
    #region Public Fields
    public float X = x;
    public float Y = y;
    public float Width = width;
    public float Height = height;

    #endregion
    #region Constructors

    public RectangleF(double x, double y, double width, double height)
        : this((float)x, (float)y, (float)width, (float)height) { }

    public RectangleF(Vector2 position, Vector2 size)
        : this(position.X, position.Y, size.X, size.Y) { }

    public RectangleF(Point location, Point size)
        : this(location.X, location.Y, size.X, size.Y) { }

    public RectangleF(Rectangle rectangle)
        : this(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height) { }
    #endregion

    #region Static Factory Methods
    public static RectangleF Empty => new(0, 0, 0, 0);

    public static RectangleF FromRectangle(Rectangle rectangle) {
        return new RectangleF(rectangle);
    }

    public static RectangleF FromCircle(Vector2 center, float radius) {
        var extent = Vector2.One * radius;
        return new RectangleF(center - extent, extent * 2);
    }

    public static RectangleF FromEllipse(Vector2 center, float xRadius, float yRadius) {
        var extent = new Vector2(xRadius, yRadius);
        return new RectangleF(center - extent, extent * 2);
    }

    public static RectangleF FromEllipse(Vector2 center, Vector2 radius) {
        return FromEllipse(center, radius.X, radius.Y);
    }

    public static RectangleF FromLTRB(float left, float top, float right, float bottom) {
        return new RectangleF(left, top, right - left, bottom - top);
    }

    public static RectangleF FromWidth(float width) => new(0, 0, width, 0);
    public static RectangleF FromHeight(float height) => new(0, 0, 0, height);
    #endregion

    #region Basic Properties
    public Vector2 Position {
        readonly get => new(X, Y);
        set {
            X = value.X;
            Y = value.Y;
        }
    }

    public Vector2 Size {
        readonly get => new(Width, Height);
        set {
            Width = value.X;
            Height = value.Y;
        }
    }

    public readonly bool IsEmpty => Width == 0 && Height == 0 && X == 0 && Y == 0;
    #endregion

    #region Boundary & Alignment Properties
    public readonly float Left => X;
    public readonly float Right => X + Width;
    public readonly float Top => Y;
    public readonly float Bottom => Y + Height;

    public readonly float CenterX => X + Width / 2;
    public readonly float CenterY => Y + Height / 2;
    public readonly Vector2 Center => new(X + Width / 2, Y + Height / 2);

    public readonly Vector2 TopLeft => new(X, Y);
    public readonly Vector2 TopRight => new(X + Width, Y);
    public readonly Vector2 BottomLeft => new(X, Y + Height);
    public readonly Vector2 BottomRight => new(X + Width, Y + Height);

    public readonly Vector2 MidLeft => new(X, Y + Height / 2);
    public readonly Vector2 MidRight => new(X + Width, Y + Height / 2);
    public readonly Vector2 MidTop => new(X + Width / 2, Y);
    public readonly Vector2 MidBottom => new(X + Width / 2, Y + Height);

    // Aliases
    public readonly Vector2 TL => TopLeft;
    public readonly Vector2 TR => TopRight;
    public readonly Vector2 BL => BottomLeft;
    public readonly Vector2 BR => BottomRight;
    public readonly (Vector2 TL, Vector2 TR, Vector2 BR, Vector2 BL) Corners => (TopLeft, TopRight, BottomRight, BottomLeft);
    #endregion

    #region Queries & Intersections
    public readonly bool Contains(Point point) => Contains(point.X, point.Y);

    public readonly bool Contains(Vector2 point) => Contains(point.X, point.Y);

    public readonly bool Contains(float x, float y) {
        return X <= x && x < X + Width && Y <= y && y < Y + Height;
    }

    public readonly bool Contains(RectangleF value) {
        return X <= value.X && value.X + value.Width <= X + Width &&
               Y <= value.Y && value.Y + value.Height <= Y + Height;
    }

    public readonly bool Contains(Rectangle value) {
        return X <= value.X && value.X + value.Width <= X + Width &&
               Y <= value.Y && value.Y + value.Height <= Y + Height;
    }

    public readonly bool Intersects(RectangleF value) {
        return value.X < X + Width && X < value.X + value.Width &&
               value.Y < Y + Height && Y < value.Y + value.Height;
    }

    public readonly bool Intersects(Rectangle value) {
        return value.X < X + Width && X < value.X + value.Width &&
               value.Y < Y + Height && Y < value.Y + value.Height;
    }

    public readonly bool Intersects(RectangleF value, out RectangleF? result) {
        result = GetIntersection(value);
        return result.HasValue;
    }

    public readonly RectangleF? GetIntersection(RectangleF value) {
        if (value.X < X + Width && X < value.X + value.Width &&
            value.Y < Y + Height && Y < value.Y + value.Height) {
            float resultX = float.Max(X, value.X);
            float resultY = float.Max(Y, value.Y);
            return new RectangleF(
                resultX,
                resultY,
                float.Min(X + Width, value.X + value.Width) - resultX,
                float.Min(Y + Height, value.Y + value.Height) - resultY
            );
        }

        return null;
    }

    public readonly Vector2 GetAnchor(Anchor anchor) {
        return anchor switch {
            Anchor.TopLeft => TopLeft,
            Anchor.TopRight => TopRight,
            Anchor.BottomLeft => BottomLeft,
            Anchor.BottomRight => BottomRight,
            Anchor.Top => new Vector2(X + Width / 2f, Y),
            Anchor.Bottom => new Vector2(X + Width / 2f, Y + Height),
            Anchor.Left => new Vector2(X, Y + Height / 2f),
            Anchor.Right => new Vector2(X + Width, Y + Height / 2f),
            Anchor.Center => new Vector2(X + Width / 2f, Y + Height / 2f),
            _ => TopLeft
        };
    }

    public static bool CloseEnough(RectangleF a, RectangleF b, float tolerance = 2f) {
        return float.Abs(a.X - b.X) <= tolerance &&
               float.Abs(a.Y - b.Y) <= tolerance &&
               float.Abs(a.Width - b.Width) <= tolerance &&
               float.Abs(a.Height - b.Height) <= tolerance;
    }
    #endregion

    #region In-place Transformations (Instance Methods)
    public void Offset(Point amount) => Offset(amount.X, amount.Y);

    public void Offset(Vector2 amount) => Offset(amount.X, amount.Y);

    public void Offset(float xAmount, float yAmount) {
        X += xAmount;
        Y += yAmount;
    }

    public void Inflate(float horizontalAmount, float verticalAmount) {
        X -= horizontalAmount;
        Y -= verticalAmount;
        Width += horizontalAmount * 2;
        Height += verticalAmount * 2;
    }

    public void Inflate(float amount) => Inflate(amount, amount);

    public void Inflate(Vector2 amount) => Inflate(amount.X, amount.Y);

    public void Scale(float scale) {
        if (scale == 1f) return;
        Vector2 center = Center;
        float newWidth = Width * scale;
        float newHeight = Height * scale;

        X = center.X - newWidth / 2.0f;
        Y = center.Y - newHeight / 2.0f;
        Width = newWidth;
        Height = newHeight;
    }

    public void Scale(Vector2 scale) {
        if (scale == Vector2.One) return;
        Vector2 center = Center;
        float newWidth = Width * scale.X;
        float newHeight = Height * scale.Y;

        X = center.X - newWidth / 2.0f;
        Y = center.Y - newHeight / 2.0f;
        Width = newWidth;
        Height = newHeight;
    }

    public void Scale(float scale, Vector2 origin) {
        if (scale == 1f) return;
        X = origin.X + (X - origin.X) * scale;
        Y = origin.Y + (Y - origin.Y) * scale;
        Width *= scale;
        Height *= scale;
    }

    public void Scale(Vector2 scale, Vector2 origin) {
        if (scale == Vector2.One) return;
        X = origin.X + (X - origin.X) * scale.X;
        Y = origin.Y + (Y - origin.Y) * scale.Y;
        Width *= scale.X;
        Height *= scale.Y;
    }

    public void FitAndCenter(float aspectRatio) {
        FitAndAlign(aspectRatio, Anchor.Center);
    }

    public void FitAndAlign(float aspectRatio, Anchor alignment = Anchor.Center) {
        float boundsAspect = Width / Height;
        float newWidth, newHeight;

        if (boundsAspect > aspectRatio) {
            newHeight = Height;
            newWidth = newHeight * aspectRatio;
        }
        else {
            newWidth = Width;
            newHeight = newWidth / aspectRatio;
        }

        X += alignment switch {
            Anchor.Left or Anchor.TopLeft or Anchor.BottomLeft => 0f,
            Anchor.Right or Anchor.TopRight or Anchor.BottomRight => Width - newWidth,
            _ => (Width - newWidth) / 2f
        };

        Y += alignment switch {
            Anchor.Top or Anchor.TopLeft or Anchor.TopRight => 0f,
            Anchor.Bottom or Anchor.BottomLeft or Anchor.BottomRight => Height - newHeight,
            _ => (Height - newHeight) / 2f
        };

        Width = newWidth;
        Height = newHeight;
    }
    #endregion

    #region Immutable Transformations (Returning a New Instance)
    public readonly RectangleF GetOffset(Point amount) => GetOffset(amount.X, amount.Y);

    public readonly RectangleF GetOffset(Vector2 amount) => GetOffset(amount.X, amount.Y);

    public readonly RectangleF GetOffset(float xAmount, float yAmount) {
        return new RectangleF(X + xAmount, Y + yAmount, Width, Height);
    }

    public readonly RectangleF GetInflated(float horizontalAmount, float verticalAmount) {
        return new RectangleF(X - horizontalAmount, Y - verticalAmount, Width + horizontalAmount * 2, Height + verticalAmount * 2);
    }

    public readonly RectangleF GetInflated(float amount) => GetInflated(amount, amount);

    public readonly RectangleF GetInflated(Vector2 amount) => GetInflated(amount.X, amount.Y);

    public readonly RectangleF GetExpanded(float horizontalAmount, float verticalAmount) {
        return new RectangleF(X, Y, Width + horizontalAmount, Height + verticalAmount);
    }

    public readonly RectangleF GetExpanded(float amount) => GetExpanded(amount, amount);

    public readonly RectangleF GetExpanded(Vector2 amount) => GetExpanded(amount.X, amount.Y);

    public readonly RectangleF GetScaled(float scale) {
        if (scale == 1f) return this;
        Vector2 center = Center;
        float newWidth = Width * scale;
        float newHeight = Height * scale;

        return new RectangleF(
            center.X - newWidth / 2.0f,
            center.Y - newHeight / 2.0f,
            newWidth,
            newHeight
        );
    }

    public readonly RectangleF GetScaled(float scaleX, float scaleY) {
        if (scaleX == 1 && scaleY == 1) return this;
        Vector2 center = Center;
        float newWidth = Width * scaleX;
        float newHeight = Height * scaleY;

        return new RectangleF(
            center.X - newWidth / 2.0f,
            center.Y - newHeight / 2.0f,
            newWidth,
            newHeight
        );
    }

    public readonly RectangleF GetScaled(Vector2 scale) => GetScaled(scale.X, scale.Y);

    public readonly RectangleF GetScaled(float scale, Vector2 origin) {
        if (scale == 1f) return this;
        return new RectangleF(
            origin.X + (X - origin.X) * scale,
            origin.Y + (Y - origin.Y) * scale,
            Width * scale,
            Height * scale
        );
    }

    public readonly RectangleF GetScaled(float scaleX, float scaleY, Vector2 origin) {
        if (scaleX == 1 && scaleY == 1) return this;
        return new RectangleF(
            origin.X + (X - origin.X) * scaleX,
            origin.Y + (Y - origin.Y) * scaleY,
            Width * scaleX,
            Height * scaleY
        );
    }

    public readonly RectangleF GetScaled(Vector2 scale, Vector2 origin) => GetScaled(scale.X, scale.Y, origin);

    public readonly RectangleF GetFitAndCentered(float aspectRatio) {
        return GetFitAndAligned(aspectRatio, Anchor.Center);
    }

    public readonly RectangleF GetFitAndAligned(float aspectRatio, Anchor alignment = Anchor.Center) {
        return FitAndAlign(this, aspectRatio, alignment);
    }
    #endregion

    #region Static Transformations
    public static RectangleF Offset(RectangleF rectangle, Point amount) => Offset(rectangle, amount.X, amount.Y);

    public static RectangleF Offset(RectangleF rectangle, Vector2 amount) => Offset(rectangle, amount.X, amount.Y);

    public static RectangleF Offset(RectangleF rectangle, float xAmount, float yAmount) {
        return new(rectangle.X + xAmount, rectangle.Y + yAmount, rectangle.Width, rectangle.Height);
    }

    public static RectangleF Inflate(RectangleF rectangle, float horizontalAmount, float verticalAmount) {
        return new(rectangle.X - horizontalAmount, rectangle.Y - verticalAmount, rectangle.Width + horizontalAmount * 2, rectangle.Height + verticalAmount * 2);
    }

    public static RectangleF Scale(RectangleF rectangle, float scale) {
        if (scale == 1f) return rectangle;
        Vector2 center = rectangle.Center;
        float newWidth = rectangle.Width * scale;
        float newHeight = rectangle.Height * scale;

        return new RectangleF(
            center.X - newWidth / 2.0f,
            center.Y - newHeight / 2.0f,
            newWidth,
            newHeight
        );
    }

    public static RectangleF Scale(RectangleF rectangle, Vector2 scale) {
        Vector2 center = rectangle.Center;
        float newWidth = rectangle.Width * scale.X;
        float newHeight = rectangle.Height * scale.Y;
        float newX = center.X - newWidth / 2.0f;
        float newY = center.Y - newHeight / 2.0f;
        return new RectangleF(newX, newY, newWidth, newHeight);
    }

    public static RectangleF Scale(RectangleF rectangle, float scale, Vector2 origin) {
        if (scale == 1f) return rectangle;
        return new RectangleF(
            origin.X + (rectangle.X - origin.X) * scale,
            origin.Y + (rectangle.Y - origin.Y) * scale,
            rectangle.Width * scale,
            rectangle.Height * scale
        );
    }

    public static RectangleF Scale(RectangleF rectangle, Vector2 scale, Vector2 origin) {
        return new RectangleF(
            origin.X + (rectangle.X - origin.X) * scale.X,
            origin.Y + (rectangle.Y - origin.Y) * scale.Y,
            rectangle.Width * scale.X,
            rectangle.Height * scale.Y
        );
    }

    public static RectangleF FitAndCenter(RectangleF bounds, float aspectRatio) {
        return FitAndAlign(bounds, aspectRatio, Anchor.Center);
    }

    public static RectangleF FitAndAlign(RectangleF bounds, float aspectRatio, Anchor alignment = Anchor.Center) {
        bounds.FitAndAlign(aspectRatio, alignment);
        return bounds;
    }

    public static RectangleF Union(RectangleF value1, RectangleF value2) {
        float x = float.Min(value1.X, value2.X);
        float y = float.Min(value1.Y, value2.Y);
        return new RectangleF(
            x, y,
            float.Max(value1.X + value1.Width, value2.X + value2.Width) - x,
            float.Max(value1.Y + value1.Height, value2.Y + value2.Height) - y
        );
    }

    public static RectangleF Lerp(RectangleF A, RectangleF B, float t) {
        float newX = A.X + (B.X - A.X) * t;
        float newY = A.Y + (B.Y - A.Y) * t;
        float newWidth = A.Width + (B.Width - A.Width) * t;
        float newHeight = A.Height + (B.Height - A.Height) * t;
        return new RectangleF(newX, newY, newWidth, newHeight);
    }
    #endregion

    #region Conversion Operations
    public readonly Rectangle ToRectangle() => ToRectangle(this);

    public static Rectangle ToRectangle(RectangleF rectangle) {
        return new Rectangle(
            (int)rectangle.X,
            (int)rectangle.Y,
            (int)rectangle.Width,
            (int)rectangle.Height
        );
    }

    public static Rectangle? ToRectangle(RectangleF? rectangle) {
        if (rectangle.HasValue)
            return ToRectangle(rectangle.Value);
        return null;
    }

    public static implicit operator RectangleF(Rectangle rect) => FromRectangle(rect);
    #endregion

    #region Equality & Standard Overrides
    public readonly bool Equals(RectangleF other) {
        return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
    }

    public readonly override bool Equals(object? obj) {
        return obj is RectangleF rect && Equals(rect);
    }

    public readonly override int GetHashCode() {
        return X.GetHashCode() ^ Y.GetHashCode() ^ Width.GetHashCode() ^ Height.GetHashCode();
    }

    public readonly override string ToString() {
        return $"{{X:{X} Y:{Y} W:{Width} H:{Height}}}";
    }

    public static bool operator ==(RectangleF a, RectangleF b) {
        return a.Equals(b);
    }

    public static bool operator !=(RectangleF a, RectangleF b) {
        return !a.Equals(b);
    }
    #endregion
}