using FontStashSharp.Interfaces;
using Microsoft.Xna.Framework.Graphics;

namespace Guilred.Rendering;

public class GuilFontStashRenderer(GuilBatch Batch) : IFontStashRenderer2 {
    public GraphicsDevice GraphicsDevice => Batch.Graphics;
    public void DrawQuad(Texture2D texture, ref VertexPositionColorTexture topLeft, ref VertexPositionColorTexture topRight, ref VertexPositionColorTexture bottomLeft, ref VertexPositionColorTexture bottomRight) {
        Batch.DrawQuad(texture, getVertex(topLeft), getVertex(topRight), getVertex(bottomRight), getVertex(bottomLeft));
    }
    private static PrimitiveVertex getVertex(VertexPositionColorTexture vpct) {
        return new PrimitiveVertex(vpct.Position, vpct.TextureCoordinate, 0, vpct.Color, default, default);
    }
}