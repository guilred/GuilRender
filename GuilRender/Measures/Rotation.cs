using Microsoft.Xna.Framework;

namespace Guilred.Rendering;

public struct Rotation(float angle, Vector2? pivot = null) {
    public float Angle = angle;
    public Vector2? Pivot = pivot;
    public readonly bool Exists => Angle % float.Tau != 0;
    public static implicit operator Rotation(float angle) => new(angle);
    public static implicit operator Rotation((float angle, Vector2? pivot) t) => new(t.angle, t.pivot);
}