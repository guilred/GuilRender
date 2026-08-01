using System;
using System.IO;
using System.Runtime.InteropServices;
using Guilred.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Guilred.Rendering;

public class Texbatch {
    private const int MaxVertices = 8192;
    private const int MaxIndices = MaxVertices * 3;

    public readonly GraphicsDevice Graphics;

    private readonly Effect _effect;
    private readonly EffectPass _pass;
    private readonly EffectParameter _projectionParam;
    private BlendState _currentBlendState = BlendState.AlphaBlend;
    private SamplerState _currentSamplerState = SamplerState.LinearClamp;

    private Texture2D? _texture;

    private readonly DynamicVertexBuffer _vertexBuffer;
    private readonly DynamicIndexBuffer _indexBuffer;

    private readonly TexVertex[] _vertices = new TexVertex[MaxVertices];
    private readonly short[] _indices = new short[MaxIndices];
    private int _vertexCount;
    private int _indexCount;

    private bool _begun;

    // debugging
    //private double d_time;
    //private bool d_blink => double.Sin(d_time * 0.5f) > 0;

    public Texbatch(GraphicsDevice device, ContentManager? content = null, Effect? effect = null) {
        Graphics = device;

        if (content is not null)
            _effect = content.Load<Effect>("texbatch-effect");
        else if (effect is not null)
            _effect = effect;
        else {
            var assembly = typeof(Texbatch).Assembly;
            string resourceName = "TexBatch.texbatch-effect.mgfx";
            using Stream? stream = assembly.GetManifestResourceStream(resourceName) ?? throw new Exception("Could not find the embedded shader resource :(");
            byte[] bytecode = new byte[stream.Length];
            stream.ReadExactly(bytecode, 0, (int)stream.Length);
            _effect = new Effect(Graphics, bytecode);
        }

        _pass = _effect.Techniques[0].Passes[0];
        _projectionParam = _effect.Parameters["Projection"];

        _vertexBuffer = new DynamicVertexBuffer(device, TexVertex.VertexDeclaration, MaxVertices, BufferUsage.WriteOnly);
        _indexBuffer = new DynamicIndexBuffer(device, IndexElementSize.SixteenBits, MaxIndices, BufferUsage.WriteOnly);
    }

    public void Begin(Matrix? view = null, Matrix? projection = null, BlendState? blendState = null, SamplerState? samplerState = null) {
        if (_begun) throw new InvalidOperationException("Texbatch is already begun.");

        updateProjection(view, projection);
        _vertexCount = 0;
        _indexCount = 0;
        _currentBlendState = blendState ?? BlendState.AlphaBlend;
        _currentSamplerState = samplerState ?? SamplerState.LinearClamp;
        _begun = true;
        //d_time += 1 / 60f;
    }

    public void SetTransform(Matrix? view = null, Matrix? projection = null) {
        ensureBegun();
        flush();
        updateProjection(view, projection);
    }

    public void SetBlendState(BlendState blendState) {
        ensureBegun();
        if (_currentBlendState == blendState) return;
        flush();
        _currentBlendState = blendState;
    }

    public void SetSamplerState(SamplerState samplerState) {
        ensureBegun();
        flush();
        _currentSamplerState = samplerState;
    }

    private void ensureBegun() {
        if (!_begun) throw new InvalidOperationException("Texbatch has not been begun.");
    }

    public void End() {
        ensureBegun();
        flush();
        _begun = false;
    }

    private void flush() {
        if (_vertexCount == 0) return;

        _vertexBuffer.SetData(_vertices, 0, _vertexCount, SetDataOptions.Discard);
        _indexBuffer.SetData(_indices, 0, _indexCount, SetDataOptions.Discard);

        Graphics.SetVertexBuffer(_vertexBuffer);
        Graphics.Indices = _indexBuffer;

        (var previousBlendState, Graphics.BlendState) = (Graphics.BlendState, _currentBlendState);
        (var previousRasterizerState, Graphics.RasterizerState) = (Graphics.RasterizerState, RasterizerState.CullNone);
        (var previousSamplerState, Graphics.SamplerStates[0]) = (Graphics.SamplerStates[0], _currentSamplerState);

        _pass.Apply();
        Graphics.Textures[0] = _texture;

        Graphics.DrawIndexedPrimitives(
            primitiveType: PrimitiveType.TriangleList,
            baseVertex: 0,
            startIndex: 0,
            primitiveCount: _indexCount / 3
        );

        Graphics.Textures[0] = null;
        Graphics.BlendState = previousBlendState;
        Graphics.RasterizerState = previousRasterizerState;
        Graphics.SamplerStates[0] = previousSamplerState;

        _vertexCount = 0;
        _indexCount = 0;
    }

    private void updateProjection(Matrix? view, Matrix? projection) {
        var currentView = view ?? Matrix.Identity;
        Matrix finalProj = currentView * (projection ?? Matrix.CreateOrthographicOffCenter(0, Graphics.Viewport.Width, Graphics.Viewport.Height, 0, 0f, 1f));
        _projectionParam.SetValue(finalProj);
    }

    private void ensureCapacity(int verticesToAdd, int indicesToAdd) {
        if (_vertexCount + verticesToAdd > MaxVertices || _indexCount + indicesToAdd > MaxIndices) {
            flush();
        }
    }
    private void updateTexture(Texture2D texture) {
        if (_texture != texture) {
            flush();
            _texture = texture;
        }
    }

    public void Draw(Texture2D texture, RectangleF rect, Color? tint = null, Rotation rotation = default, Rectangle? sourceRect = null, SpriteEffects effects = SpriteEffects.None) {
        ensureBegun();
        updateTexture(texture);
        ensureCapacity(4, 6);

        var actualTint = tint ?? Color.White;
        var (tl, tr, br, bl) = (rect.TL, rect.TR, rect.BR, rect.BL);
        if (rotation.Exists) {
            var pivot = rotation.Pivot ?? rect.Center;
            tl.RotateAround(pivot, rotation.Angle);
            tr.RotateAround(pivot, rotation.Angle);
            br.RotateAround(pivot, rotation.Angle);
            bl.RotateAround(pivot, rotation.Angle);
        }

        Vector2 uvTL, uvTR, uvBR, uvBL;
        if (sourceRect.HasValue) {
            var src = sourceRect.Value;
            float left = (float)src.X / texture.Width;
            float top = (float)src.Y / texture.Height;
            float right = (float)(src.X + src.Width) / texture.Width;
            float bottom = (float)(src.Y + src.Height) / texture.Height;
            uvTL = new Vector2(left, top);
            uvTR = new Vector2(right, top);
            uvBR = new Vector2(right, bottom);
            uvBL = new Vector2(left, bottom);
        }
        else {
            uvTL = Vector2.Zero;
            uvTR = Vector2.UnitX;
            uvBR = Vector2.One;
            uvBL = Vector2.UnitY;
        }

        if ((effects & SpriteEffects.FlipHorizontally) != 0) {
            (uvTL.X, uvTR.X) = (uvTR.X, uvTL.X);
            (uvBL.X, uvBR.X) = (uvBR.X, uvBL.X);
        }
        if ((effects & SpriteEffects.FlipVertically) != 0) {
            (uvTL.Y, uvBL.Y) = (uvBL.Y, uvTL.Y);
            (uvTR.Y, uvBR.Y) = (uvBR.Y, uvTR.Y);
        }

        short i = (short)_vertexCount;
        _vertices[_vertexCount++] = new TexVertex(tl, actualTint, uvTL);
        _vertices[_vertexCount++] = new TexVertex(tr, actualTint, uvTR);
        _vertices[_vertexCount++] = new TexVertex(br, actualTint, uvBR);
        _vertices[_vertexCount++] = new TexVertex(bl, actualTint, uvBL);

        _indices[_indexCount++] = i;
        _indices[_indexCount++] = (short)(i + 1);
        _indices[_indexCount++] = (short)(i + 2);
        _indices[_indexCount++] = i;
        _indices[_indexCount++] = (short)(i + 2);
        _indices[_indexCount++] = (short)(i + 3);
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TexVertex(Vector2 pos, Color color, Vector2 texCoords) : IVertexType {
        public Vector2 Position = pos;
        public Color Color = color;
        public Vector2 TexCoords = new(texCoords.X, texCoords.Y);

        public static readonly VertexDeclaration VertexDeclaration = new(
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
            new VertexElement(8, VertexElementFormat.Color, VertexElementUsage.Color, 0),
            new VertexElement(12, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0)
        );

        readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }
}