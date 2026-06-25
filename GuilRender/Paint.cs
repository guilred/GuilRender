using Microsoft.Xna.Framework;

namespace Guilred.Rendering;

public struct Paint {
    public enum PaintType : byte { Solid, Linear, Radial, Texture }
    public enum EasingType : byte { Linear, EaseIn, EaseOut, EaseInOut }
    public PaintType Type;
    public EasingType Easing;
    public float EasingPower;
    public Color ColorA;
    public Color ColorB;
    public Vector2 Start;
    public Vector2 End;
    public float OffsetA;
    public float OffsetB;
    public bool IsLocal;
    public bool isNormalized;
    public bool isPixelOffsets;
    public static Paint Solid(Color color) => new() { Type = PaintType.Solid, ColorA = color, ColorB = color };
    public static Paint Linear(Vector2 start, Vector2 end, Color startColor, Color endColor) {
        return new Paint() {
            Type = PaintType.Linear,
            ColorA = startColor,
            ColorB = endColor,
            Start = start,
            End = end,
            isNormalized = true,
            IsLocal = true,
            OffsetB = 1f
        };
    }
    public static Paint LinearPixel(Vector2 start, Vector2 end, Color startColor, Color endColor, bool isLocal = true) {
        return new Paint() {
            Type = PaintType.Linear,
            ColorA = startColor,
            ColorB = endColor,
            Start = start,
            End = end,
            IsLocal = isLocal,
            OffsetB = 1f
        };
    }
    public static Paint Radial(Vector2 center, Vector2 edge, Color centerColor, Color edgeColor) {
        return new Paint() {
            Type = PaintType.Radial,
            ColorA = centerColor,
            ColorB = edgeColor,
            Start = center,
            End = edge,
            isNormalized = true,
            IsLocal = true,
            OffsetB = 1f
        };
    }
    public static Paint RadialPixel(Vector2 center, Vector2 edge, Color centerColor, Color edgeColor, bool isLocal = true) {
        return new Paint() {
            Type = PaintType.Radial,
            ColorA = centerColor,
            ColorB = edgeColor,
            Start = center,
            End = edge,
            IsLocal = isLocal,
            OffsetB = 1f
        };
    }
    public Paint SetOffsets(double offsetA = 0f, double offsetB = 0f, bool usePixelOffsets = false) {
        var (dA, dB) = ((float)offsetA, (float)offsetB);
        if (!usePixelOffsets) {
            (OffsetA, OffsetB) = (dA, 1 - dB);
            return this;
        }
        if (isNormalized) {
            (OffsetA, OffsetB) = (dA, 1 - dB);
            isPixelOffsets = true;
            return this;
        }

        float distance = Vector2.Distance(Start, End);
        if (distance > 0.0001f) {
            (OffsetA, OffsetB) = (dA / distance, (distance - dB) / distance);
            return this;
        }
        (OffsetA, OffsetB) = (0f, 1f);
        return this;
    }
    public Paint SetEasing(EasingType easing = EasingType.Linear, float power = 2f) {
        Easing = easing;
        EasingPower = power;
        return this;
    }
    public static Paint Lerp(Paint a, Paint b, float amount) {
        return new Paint {
            Type = amount < 1 ? a.Type : b.Type,
            Easing = amount < 1 ? a.Easing : b.Easing,
            IsLocal = amount < 1 ? a.IsLocal : b.IsLocal,
            EasingPower = float.Lerp(a.EasingPower, b.EasingPower, amount),
            ColorA = Color.Lerp(a.ColorA, b.ColorA, amount),
            ColorB = Color.Lerp(a.ColorB, b.ColorB, amount),
            Start = Vector2.Lerp(a.Start, b.Start, amount),
            End = Vector2.Lerp(a.End, b.End, amount),
            OffsetA = float.Lerp(a.OffsetA, b.OffsetA, amount),
            OffsetB = float.Lerp(a.OffsetB, b.OffsetB, amount),
        };
    }
    public Paint Reversed() {
        return this with { Start = End, End = Start, OffsetA = OffsetB, OffsetB = OffsetA };
    }
    public Paint HueShift(float dHue) {
        if (dHue == 0) return this;
        return this with { ColorA = ShiftHue(ColorA, dHue), ColorB = ShiftHue(ColorB, dHue) };
    }
    public Paint HueShift(float dHueA, float dHueB) {
        if (dHueA == 0 && dHueB == 0) return this;
        return this with { ColorA = ShiftHue(ColorA, dHueA), ColorB = ShiftHue(ColorB, dHueB) };
    }

    public static Color ShiftHue(Color c, float degrees) {
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
        float max = float.Max(r, float.Max(g, b));
        float min = float.Min(r, float.Min(g, b));
        float delta = max - min;

        float h = 0f;
        if (delta > 0.00001f) {
            if (max == r) h = 60f * (((g - b) / delta) % 6f);
            else if (max == g) h = 60f * (((b - r) / delta) + 2f);
            else h = 60f * (((r - g) / delta) + 4f);
        }
        if (h < 0f) h += 360f;

        float s = max <= 0.00001f ? 0f : delta / max;
        float v = max;

        h = (h + degrees) % 360f;
        if (h < 0f) h += 360f;

        float chroma = v * s;
        float x = chroma * (1f - float.Abs((h / 60f) % 2f - 1f));
        float m = v - chroma;

        float rr, gg, bb;
        if (h < 60f) { rr = chroma; gg = x; bb = 0f; }
        else if (h < 120f) { rr = x; gg = chroma; bb = 0f; }
        else if (h < 180f) { rr = 0f; gg = chroma; bb = x; }
        else if (h < 240f) { rr = 0f; gg = x; bb = chroma; }
        else if (h < 300f) { rr = x; gg = 0f; bb = chroma; }
        else { rr = chroma; gg = 0f; bb = x; }

        return new Color(rr + m, gg + m, bb + m, c.A / 255f);
    }
    public bool IsOpaque() => ColorA.A == 255 && ColorB.A == 255;
    public bool IsTransparent() => ColorA.A == 0 && ColorB.A == 0;
    public static Paint operator *(Paint l, float r) {
        return l with { ColorA = l.ColorA * r, ColorB = l.ColorB * r };
    }
    public static implicit operator Paint(Color color) => Solid(color);
    public readonly override string ToString() {
        static string toHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";

        string mode = IsLocal ? "Local" : "World";
        string norm = isNormalized ? "|Norm" : "";
        string pix = isPixelOffsets ? "|PxOffs" : "";
        string flags = $"({mode}{norm}{pix})";

        string easingInfo = Easing != EasingType.Linear
            ? $", Ease: {Easing}({EasingPower:F1})" : "";

        string offsetInfo = (OffsetA != 0f || OffsetB != 1f)
            ? $", Offs: [{OffsetA:F2}, {OffsetB:F2}]" : "";

        return Type switch {
            PaintType.Solid =>
                $"[Paint: Solid {toHex(ColorA)} {flags}]",

            PaintType.Linear =>
                $"[Paint: Linear {toHex(ColorA)}->{toHex(ColorB)}, {Start}->{End}{easingInfo}{offsetInfo} {flags}]",

            PaintType.Radial =>
                $"[Paint: Radial {toHex(ColorA)}->{toHex(ColorB)}, Center:{Start} Edge:{End}{easingInfo}{offsetInfo} {flags}]",

            PaintType.Texture =>
                $"[Paint: Texture {Start}->{End} {flags}]",

            _ => $"[Paint: {Type} {flags}]"
        };
    }
}