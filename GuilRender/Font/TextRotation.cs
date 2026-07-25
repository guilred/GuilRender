using Microsoft.Xna.Framework;

namespace Guilred.Rendering;

public struct TextRotation(float angle, Vector2? pivot = null) {
    public float Angle = angle;
    public Vector2? Pivot = pivot;
    public static implicit operator TextRotation(float angle) => new(angle);
    public static implicit operator TextRotation((float angle, Vector2 pivot) t) => new(t.angle, t.pivot);
}
