using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Guilred.Rendering;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GuilVertex : IVertexType {
    public Vector3 Position;
    public Vector4 ClipRect;
    public Vector3 ClipParams;
    public Color ColorA;
    public Color ColorB;
    public Vector3 TexCoords;
    public Vector4 GradientCoords;
    public Vector3 PaintParams;

    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(28, VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 1),
        new VertexElement(40, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(44, VertexElementFormat.Color, VertexElementUsage.Color, 1),
        new VertexElement(48, VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 2),
        new VertexElement(60, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3),
        new VertexElement(76, VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 4)
    );

    public GuilVertex(Vector3 pos, in Paint paint, Vector4 clipRect, Vector3 clipParams) {
        Position = pos;
        ClipRect = clipRect;
        ClipParams = clipParams;
        ColorA = paint.ColorA;
        ColorB = paint.ColorB;
        TexCoords = new Vector3(0, 0, -1);
        GradientCoords = new Vector4(paint.Start.X, paint.Start.Y, paint.End.X, paint.End.Y);
        float safePower = float.Clamp(paint.EasingPower, 0f, 99.9f);
        float packedData = ((float)paint.Type * 1000f) + ((float)paint.Easing * 100f) + safePower;
        PaintParams = new Vector3(paint.OffsetA, paint.OffsetB, packedData);
    }

    public GuilVertex(Vector3 pos, Vector2 texCoords, int index, in Paint paint, Vector4 clipRect, Vector3 clipParams) {
        Position = pos;
        ClipRect = clipRect;
        ClipParams = clipParams;
        ColorA = paint.ColorA;
        ColorB = paint.ColorB;
        TexCoords = new Vector3(texCoords.X, texCoords.Y, index);
        GradientCoords = new Vector4(paint.Start.X, paint.Start.Y, paint.End.X, paint.End.Y);
        float safePower = float.Clamp(paint.EasingPower, 0f, 99.9f);
        float packedData = ((float)paint.Type * 1000f) + ((float)paint.Easing * 100f) + safePower;
        PaintParams = new Vector3(paint.OffsetA, paint.OffsetB, packedData);
    }

    readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
}