using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Guilred.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Guilred.Rendering;

public class GuilBatch {
    private const int _maxVertices = 8192;
    private const int _maxIndices = _maxVertices * 3;

    private readonly GraphicsDevice _device;

    private readonly Effect _effect;
    private readonly EffectPass _pass;
    private readonly EffectParameter _projectionParam;
    private readonly EffectParameter _clipSmoothingParam;
    private BlendState _currentBlendState = BlendState.AlphaBlend;
    private SamplerState _currentSamplerState = SamplerState.LinearClamp;
    private readonly SamplerState[] _prevSamplerStatesBuffer = new SamplerState[8];

    private readonly Texture2D?[] _textures = new Texture2D[8];
    private int _textureCount = 0;

    private readonly DynamicVertexBuffer _vertexBuffer;
    private readonly DynamicIndexBuffer _indexBuffer;

    private readonly PrimitiveVertex[] _vertices = new PrimitiveVertex[_maxVertices];
    private readonly short[] _indices = new short[_maxIndices];
    private int _vertexCount;
    private int _indexCount;

    private bool _begun;
    public float CameraZoom { get; private set; }

    // debugging
    private double d_time;
    private bool d_blink => double.Sin(d_time * 0.5f) > 0;
    public GuilBatch(GraphicsDevice device, ContentManager? content = null, Effect? effect = null) {
        _device = device;

        if (content is not null)
            _effect = content.Load<Effect>("guilbatch-effect");
        else if (effect is not null)
            _effect = effect;
        else {
            var assembly = typeof(GuilBatch).Assembly;
            string resourceName = "GuilBatch.guilbatch-effect.mgfx";
            using Stream? stream = assembly.GetManifestResourceStream(resourceName) ?? throw new Exception("Could not find the embedded shader resource :(");
            byte[] bytecode = new byte[stream.Length];
            stream.ReadExactly(bytecode, 0, (int)stream.Length);
            _effect = new Effect(_device, bytecode);
        }

        _pass = _effect.Techniques[0].Passes[0];
        _projectionParam = _effect.Parameters["Projection"];
        _clipSmoothingParam = _effect.Parameters["clipSmoothing"];

        _vertexBuffer = new DynamicVertexBuffer(device, PrimitiveVertex.VertexDeclaration, _maxVertices, BufferUsage.WriteOnly);
        _indexBuffer = new DynamicIndexBuffer(device, IndexElementSize.SixteenBits, _maxIndices, BufferUsage.WriteOnly);
    }
    public void Begin(Matrix? view = null, Matrix? projection = null, BlendState? blendState = null, SamplerState? samplerState = null, float? clipSmoothing = null) {
        if (_begun) throw new InvalidOperationException("Guilbatch is already begun.");

        updateProjection(view, projection);
        _clipSmoothingParam.SetValue(clipSmoothing ?? 0.5f);
        _vertexCount = 0;
        _indexCount = 0;
        _currentBlendState = blendState ?? BlendState.AlphaBlend;
        _currentSamplerState = samplerState ?? SamplerState.LinearClamp;
        _begun = true;
        d_time += 1 / 60f;
        _currentClip = new ClipState { Rect = new(0, 0, -1, 0), Params = Vector2.Zero };
    }
    public void SetTransform(Matrix? view = null, Matrix? projection = null) {
        ensureBegun();
        flush();
        updateProjection(view, projection);
    }
    public void SetBlendState(BlendState blendState) {
        ensureBegun();
        flush();
        _currentBlendState = blendState;
    }
    public void SetSamplerState(SamplerState samplerState) {
        ensureBegun();
        flush();
        _currentSamplerState = samplerState;
    }
    private void ensureBegun() {
        if (!_begun) throw new InvalidOperationException("Guilbatch has not been begun.");
    }
    public void End(bool maintainClipRects = false) {
        ensureBegun();

        flush();
        _begun = false;
        if (!maintainClipRects) {
            _clipStack.Clear();
        }
        else if (_clipStack.Count > _maxClips) {
            while (_clipStack.Count > _maxClips) {
                _clipStack.Pop();
            }
        }
    }

    private void flush() {
        if (_vertexCount == 0) return;

        _vertexBuffer.SetData(_vertices, 0, _vertexCount, SetDataOptions.Discard);
        _indexBuffer.SetData(_indices, 0, _indexCount, SetDataOptions.Discard);

        _device.SetVertexBuffer(_vertexBuffer);
        _device.Indices = _indexBuffer;

        (var previousBlendState, _device.BlendState) = (_device.BlendState, _currentBlendState);
        (var previousRasterizerState, _device.RasterizerState) = (_device.RasterizerState, RasterizerState.CullNone);

        _pass.Apply();
        for (int i = 0; i < _textureCount; i++) {
            _device.Textures[i] = _textures[i];
            _prevSamplerStatesBuffer[i] = _device.SamplerStates[i];
            _device.SamplerStates[i] = _currentSamplerState;
        }


        _device.DrawIndexedPrimitives(
            primitiveType: PrimitiveType.TriangleList,
            baseVertex: 0,
            startIndex: 0,
            primitiveCount: _indexCount / 3
        );

        for (int i = 0; i < _textureCount; i++) {
            _textures[i] = null;
            _device.SamplerStates[i] = _prevSamplerStatesBuffer[i];
        }
        _device.BlendState = previousBlendState;
        _device.RasterizerState = previousRasterizerState;

        _vertexCount = 0;
        _indexCount = 0;
        _textureCount = 0;
    }
    private void updateProjection(Matrix? view, Matrix? projection) {
        var currentView = view ?? Matrix.Identity;
        CameraZoom = new Vector3(currentView.M11, currentView.M12, currentView.M13).Length();
        Matrix finalProj = currentView * (projection ?? Matrix.CreateOrthographicOffCenter(0, _device.Viewport.Width, _device.Viewport.Height, 0, 0f, 1f));
        _projectionParam.SetValue(finalProj);
    }

    private void ensureCapacity(int verticesToAdd, int indicesToAdd) {
        if (_vertexCount + verticesToAdd > _maxVertices || _indexCount + indicesToAdd > _maxIndices) {
            flush();
        }
    }

    private record struct ClipState(Vector4 Rect, Vector2 Params);

    private const int _maxClips = 2048;
    private readonly Stack<ClipState> _clipStack = new();
    private ClipState _currentClip = new() { Rect = Vector4.Zero, Params = Vector2.Zero };
    private ClipState? _previousClip;

    public void PushClip(RectangleF clipRect, float rounding = 0f, float rotation = 0f, bool intersect = true, bool push = true) {
        if (!push) return;
        Vector4 newRect = new(clipRect.Position.X, clipRect.Position.Y, clipRect.Width, clipRect.Height);

        if (intersect && _clipStack.Count > 0 && _currentClip.Rect.Z > 0 && _currentClip.Rect.W > 0) {
            float x1 = float.Max(_currentClip.Rect.X, newRect.X);
            float y1 = float.Max(_currentClip.Rect.Y, newRect.Y);
            float x2 = float.Min(_currentClip.Rect.X + _currentClip.Rect.Z, newRect.X + newRect.Z);
            float y2 = float.Min(_currentClip.Rect.Y + _currentClip.Rect.W, newRect.Y + newRect.W);

            if (x2 >= x1 && y2 >= y1) {
                newRect = new Vector4(x1, y1, x2 - x1, y2 - y1);
            }
            else {
                newRect = Vector4.Zero;
            }
        }

        _currentClip = new ClipState { Rect = newRect, Params = new Vector2(rounding, rotation) };
        _clipStack.Push(_currentClip);
    }
    public void UnPopClip(bool unpop = true) {
        if (!unpop) return;
        if (_previousClip is null) return;
        _clipStack.Push(_previousClip.Value);
        _currentClip = _previousClip.Value;
        _previousClip = null;
    }
    public void PopClip(bool pop = true) {
        if (!pop) return;
        if (_clipStack.Count > 0) {
            _previousClip = _clipStack.Pop();
        }
        _currentClip = _clipStack.Count > 0
            ? _clipStack.Peek()
            : new ClipState { Rect = new(0, 0, -1, 0), Params = Vector2.Zero };
    }
    private int getTextureIndex(Texture2D texture) {
        for (int i = 0; i < _textureCount; i++) {
            if (_textures[i] == texture) return i;
        }

        if (_textureCount >= 8) {
            flush();
        }

        _textures[_textureCount] = texture;
        return _textureCount++;
    }
    private void addRingSegment(Vector2 center, float innerRadius, float outerRadius, float startAngle, float endAngle, Paint paint, int segments) {
        if (segments < 1 || paint.ColorA.A == 0 || outerRadius <= 0) return;

        if (innerRadius <= 0.001f) {
            ensureCapacity(segments + 2, segments * 3);
            int startIdx = _vertexCount;

            _vertices[_vertexCount++] = new PrimitiveVertex(new Vector3(center, 0), paint, _currentClip.Rect, _currentClip.Params);

            for (int i = 0; i <= segments; i++) {
                float angle = float.Lerp(startAngle, endAngle, (float)i / segments);
                (float sin, float cos) = float.SinCos(angle);
                Vector2 pos = center + new Vector2(cos, sin) * outerRadius;

                _vertices[_vertexCount++] = new PrimitiveVertex(new Vector3(pos, 0), paint, _currentClip.Rect, _currentClip.Params);

                if (i > 0) {
                    _indices[_indexCount++] = (short)startIdx;
                    _indices[_indexCount++] = (short)(startIdx + i);
                    _indices[_indexCount++] = (short)(startIdx + i + 1);
                }
            }
        }
        else {
            ensureCapacity((segments + 1) * 2, segments * 6);
            int startIdx = _vertexCount;

            for (int i = 0; i <= segments; i++) {
                float angle = float.Lerp(startAngle, endAngle, (float)i / segments);
                (float sin, float cos) = float.SinCos(angle);
                Vector2 dir = new(cos, sin);

                _vertices[_vertexCount++] = new PrimitiveVertex(new Vector3(center + dir * innerRadius, 0), paint, _currentClip.Rect, _currentClip.Params);
                _vertices[_vertexCount++] = new PrimitiveVertex(new Vector3(center + dir * outerRadius, 0), paint, _currentClip.Rect, _currentClip.Params);

                if (i > 0) {
                    int v0 = startIdx + (i - 1) * 2;
                    int v1 = v0 + 1;
                    int v2 = startIdx + i * 2;
                    int v3 = v2 + 1;

                    _indices[_indexCount++] = (short)v0;
                    _indices[_indexCount++] = (short)v1;
                    _indices[_indexCount++] = (short)v2;

                    _indices[_indexCount++] = (short)v1;
                    _indices[_indexCount++] = (short)v3;
                    _indices[_indexCount++] = (short)v2;
                }
            }
        }
    }

    private void addRectFringe(Span<Vector2> centers, float radius, int cornerSegments, Paint paint, bool outer, bool hasRotation, float rotSin, float rotCos, Vector2 pivot, float aaSize) {
        int perimeterVerts = (cornerSegments + 1) * 4;
        ensureCapacity(perimeterVerts * 2, perimeterVerts * 6);

        float step = MathHelper.PiOver2 / cornerSegments;
        int fringeStart = _vertexCount;

        for (int c = 0; c < 4; c++) {
            float startAngle = c * MathHelper.PiOver2;
            for (int i = 0; i <= cornerSegments; i++) {
                float angle = startAngle + i * step;
                (float sin, float cos) = float.SinCos(angle);
                Vector2 dir = new(cos, sin);
                Vector2 basePos = centers[c] + dir * radius;

                Vector2 worldBase, worldFringe;
                if (hasRotation) {
                    float rx = basePos.X - pivot.X, ry = basePos.Y - pivot.Y;
                    worldBase = new Vector2(pivot.X + rx * rotCos - ry * rotSin, pivot.Y + rx * rotSin + ry * rotCos);

                    float wdx = (dir.X * rotCos - dir.Y * rotSin) * aaSize;
                    float wdy = (dir.X * rotSin + dir.Y * rotCos) * aaSize;
                    worldFringe = outer
                        ? new Vector2(worldBase.X + wdx, worldBase.Y + wdy)
                        : new Vector2(worldBase.X - wdx, worldBase.Y - wdy);
                }
                else {
                    worldBase = basePos;
                    Vector2 aaDir = dir * aaSize;
                    worldFringe = outer ? basePos + aaDir : basePos - aaDir;
                }

                _vertices[_vertexCount++] = new PrimitiveVertex(new Vector3(worldBase, 0), paint, _currentClip.Rect, _currentClip.Params);

                _vertices[_vertexCount++] = new PrimitiveVertex(new Vector3(worldFringe, 0), paint, _currentClip.Rect, _currentClip.Params) {
                    ColorA = Color.Transparent,
                    ColorB = Color.Transparent
                };
            }
        }

        for (int k = 0; k < perimeterVerts; k++) {
            int next = (k + 1) % perimeterVerts;
            int v0 = fringeStart + k * 2;
            int v1 = fringeStart + k * 2 + 1;
            int v2 = fringeStart + next * 2;
            int v3 = fringeStart + next * 2 + 1;

            _indices[_indexCount++] = (short)v0;
            _indices[_indexCount++] = (short)v1;
            _indices[_indexCount++] = (short)v2;

            _indices[_indexCount++] = (short)v1;
            _indices[_indexCount++] = (short)v3;
            _indices[_indexCount++] = (short)v2;
        }
    }
    public void DrawRectangle(Vector2 position, Vector2 size, Paint fillPaint, Paint borderPaint, float borderThickness, float rounding = 0, float rotation = 0f, Vector2? origin = null, ArcQuality cornerQuality = ArcQuality.Normal, float aaSize = 1f) {
        ensureBegun();
        if (size.X <= 0 || size.Y <= 0) return;

        float minHalf = float.Min(size.X, size.Y) * 0.5f;
        rounding = float.Clamp(rounding, 0f, minHalf);
        borderThickness = float.Clamp(borderThickness, 0f, minHalf);

        var usedOrigin = origin ?? size / 2;
        borderPaint = transformPaint(borderPaint, position + usedOrigin, -usedOrigin, rotation, size);
        var padding = Vector2.One * borderThickness;
        fillPaint = transformPaint(fillPaint, position + padding + usedOrigin, -usedOrigin, rotation, size - padding * 2);

        bool hasFill = borderThickness < minHalf && !fillPaint.IsTransparent();
        bool hasBorder = borderThickness > 0f && !borderPaint.IsTransparent();

        if (!hasFill && !hasBorder) return;

        var cornerSegments = rounding > 0 ? computeSegments(rounding, MathHelper.PiOver2, cornerQuality) : 1;
        int perimVerts = (cornerSegments + 1) * 4;
        float arcStep = MathHelper.PiOver2 / cornerSegments;

        Span<float> cornerStartAngles = [0f, MathHelper.PiOver2, float.Pi, float.Pi * 1.5f];

        float outR = rounding;
        Span<Vector2> outerCenters = [
            position + new Vector2(size.X - outR, size.Y - outR),
            position + new Vector2(outR, size.Y - outR),
            position + new Vector2(outR, outR),
            position + new Vector2(size.X - outR, outR),
        ];

        float inR = float.Max(0f, rounding - borderThickness);
        Vector2 innerPos = position + new Vector2(borderThickness, borderThickness);
        Vector2 innerSize = size - new Vector2(borderThickness * 2f, borderThickness * 2f);
        Span<Vector2> innerCenters = [
            innerPos + new Vector2(innerSize.X - inR, innerSize.Y - inR),
            innerPos + new Vector2(inR, innerSize.Y - inR),
            innerPos + new Vector2(inR, inR),
            innerPos + new Vector2(innerSize.X - inR, inR),
        ];

        Vector2 pivot = position + usedOrigin;
        bool hasRotation = rotation != 0f;
        float rotSin = 0f, rotCos = 1f;
        if (hasRotation) (rotSin, rotCos) = float.SinCos(rotation);

        Vector2 Rotate(Vector2 p) {
            if (!hasRotation) return p;
            float dx = p.X - pivot.X, dy = p.Y - pivot.Y;
            return new Vector2(
                pivot.X + dx * rotCos - dy * rotSin,
                pivot.Y + dx * rotSin + dy * rotCos);
        }

        PrimitiveVertex Vert(Vector2 p, Paint paint) => new(new Vector3(p, 0f), paint, _currentClip.Rect, _currentClip.Params);

        if (hasFill) {
            ensureCapacity(perimVerts + 1, perimVerts * 3);
            int baseIdx = _vertexCount;

            _vertices[_vertexCount++] = Vert(Rotate(innerPos + innerSize * 0.5f), fillPaint);

            for (int c = 0; c < 4; c++) {
                for (int i = 0; i <= cornerSegments; i++) {
                    float angle = cornerStartAngles[c] + i * arcStep;
                    (float sin, float cos) = float.SinCos(angle);
                    _vertices[_vertexCount++] = Vert(Rotate(innerCenters[c] + new Vector2(cos, sin) * inR), fillPaint);
                }
            }

            for (int v = 0; v < perimVerts; v++) {
                _indices[_indexCount++] = (short)baseIdx;
                _indices[_indexCount++] = (short)(baseIdx + 1 + v);
                _indices[_indexCount++] = (short)(baseIdx + 1 + (v + 1) % perimVerts);
            }
        }

        if (hasBorder) {
            ensureCapacity(perimVerts * 2, perimVerts * 6);
            int baseIdx = _vertexCount;

            for (int c = 0; c < 4; c++) {
                for (int i = 0; i <= cornerSegments; i++) {
                    float angle = cornerStartAngles[c] + i * arcStep;
                    (float sin, float cos) = float.SinCos(angle);
                    Vector2 dir = new(cos, sin);

                    _vertices[_vertexCount++] = Vert(Rotate(innerCenters[c] + dir * inR), borderPaint);
                    _vertices[_vertexCount++] = Vert(Rotate(outerCenters[c] + dir * outR), borderPaint);
                }
            }

            for (int v = 0; v < perimVerts; v++) {
                int next = (v + 1) % perimVerts;
                int inner0 = baseIdx + v * 2;
                int outer0 = baseIdx + v * 2 + 1;
                int inner1 = baseIdx + next * 2;
                int outer1 = baseIdx + next * 2 + 1;

                _indices[_indexCount++] = (short)inner0;
                _indices[_indexCount++] = (short)outer0;
                _indices[_indexCount++] = (short)inner1;

                _indices[_indexCount++] = (short)outer0;
                _indices[_indexCount++] = (short)outer1;
                _indices[_indexCount++] = (short)inner1;
            }
        }

        if (aaSize == 0f) return;
        aaSize /= CameraZoom;

        Vector2 aaPivot = position + usedOrigin;

        if (hasBorder) {
            addRectFringe(outerCenters, outR, cornerSegments, borderPaint, true, hasRotation, rotSin, rotCos, aaPivot, aaSize);
            addRectFringe(innerCenters, inR, cornerSegments, borderPaint, false, hasRotation, rotSin, rotCos, aaPivot, aaSize);
        }
        if (hasFill) {
            float borderA = hasBorder ? byte.Max(borderPaint.ColorA.A, borderPaint.ColorB.A) / 255f : 0;
            addRectFringe(innerCenters, inR, cornerSegments, fillPaint * (1 - borderA), true, hasRotation, rotSin, rotCos, aaPivot, aaSize);
        }
    }

    public void FillRectangle(Vector2 position, Vector2 size, Paint fillPaint, float rounding = 0f, float rotation = 0f, Vector2? origin = null, ArcQuality cornerQuality = ArcQuality.Normal, float aaSize = 1f)
        => DrawRectangle(position, size, fillPaint, default, 0, rounding, rotation, origin, cornerQuality, aaSize);

    public void BorderRectangle(Vector2 position, Vector2 size, Paint borderPaint, float borderThickness, float rounding = 0f, float rotation = 0f, Vector2? origin = null, ArcQuality cornerQuality = ArcQuality.Normal, float aaSize = 1f)
        => DrawRectangle(position, size, default, borderPaint, borderThickness, rounding, rotation, origin, cornerQuality, aaSize);

    public void DrawLine(Vector2 start, Vector2 end, Paint fillPaint, Paint borderPaint, float thickness, float borderThickness, ArcQuality capQuality = ArcQuality.Normal, float aaSize = 1f) {
        ensureBegun();
        if (thickness <= 0) return;

        Vector2 dir = end - start;
        float length = dir.Length();
        float angle = float.Atan2(dir.Y, dir.X);

        Vector2 size = new(length + thickness, thickness);

        Vector2 origin = new(thickness * 0.5f, thickness * 0.5f);
        Vector2 position = start - origin;

        if (!fillPaint.isNormalized)
            fillPaint = transformPaint(fillPaint, origin, Vector2.Zero, 0);
        if (!borderPaint.isNormalized)
            borderPaint = transformPaint(borderPaint, origin, Vector2.Zero, 0);

        DrawRectangle(position, size, fillPaint, borderPaint, borderThickness, rounding: thickness * 0.5f, angle, origin, capQuality, aaSize);
    }

    public void FillLine(Vector2 start, Vector2 end, Paint paint, float thickness, ArcQuality capQuality = ArcQuality.Normal, float aaSize = 1f)
        => DrawLine(start, end, paint, default, thickness, 0f, capQuality, aaSize);

    public void BorderLine(Vector2 start, Vector2 end, Paint borderPaint, float thickness, float borderThickness, ArcQuality capQuality = ArcQuality.Normal, float aaSize = 1f)
        => DrawLine(start, end, default, borderPaint, thickness, borderThickness, capQuality, aaSize);

    private void addCircleFringe(Vector2 center, float radius, float startAngle, float endAngle, Paint paint, int segments, bool outer, float aaSize) {
        if (segments < 1 || radius <= 0f) return;

        float fringeRadius = outer ? radius + aaSize : float.Max(0f, radius - aaSize);
        if (!outer && fringeRadius >= radius) return;

        ensureCapacity((segments + 1) * 2, segments * 6);
        int fringeStart = _vertexCount;

        for (int i = 0; i <= segments; i++) {
            float angle = float.Lerp(startAngle, endAngle, (float)i / segments);
            (float sin, float cos) = float.SinCos(angle);
            Vector2 dir = new(cos, sin);

            _vertices[_vertexCount++] = new PrimitiveVertex(new Vector3(center + dir * radius, 0), paint, _currentClip.Rect, _currentClip.Params);

            _vertices[_vertexCount++] = new PrimitiveVertex(new Vector3(center + dir * fringeRadius, 0), paint, _currentClip.Rect, _currentClip.Params) {
                ColorA = Color.Transparent,
                ColorB = Color.Transparent
            };
        }

        for (int i = 0; i < segments; i++) {
            int v0 = fringeStart + i * 2;
            int v1 = fringeStart + i * 2 + 1;
            int v2 = fringeStart + (i + 1) * 2;
            int v3 = fringeStart + (i + 1) * 2 + 1;

            _indices[_indexCount++] = (short)v0;
            _indices[_indexCount++] = (short)v1;
            _indices[_indexCount++] = (short)v2;

            _indices[_indexCount++] = (short)v1;
            _indices[_indexCount++] = (short)v3;
            _indices[_indexCount++] = (short)v2;
        }
    }
    public void DrawArc(Vector2 center, Paint fillPaint, Paint borderPaint, float innerRadius, float outerRadius, float startAngle, float endAngle, float borderThickness, ArcQuality quality = ArcQuality.Normal, float aaSize = 1f) {
        ensureBegun();
        static float normalizeAngle(float angle) => ((angle % float.Tau) + float.Tau) % float.Tau;

        startAngle = normalizeAngle(startAngle);
        endAngle = normalizeAngle(endAngle);
        if (endAngle <= startAngle) endAngle += float.Tau;

        var segments = computeSegments(innerRadius + outerRadius, endAngle - startAngle, quality);
        if (outerRadius <= 0f || segments < 3) return;

        float outerEdge = innerRadius + outerRadius;
        float midRadius = innerRadius + outerRadius * 0.5f;
        float halfThick = outerRadius * 0.5f;

        float borderThick = float.Min(borderThickness, halfThick);
        float fillHalfThick = float.Max(0f, halfThick - borderThick);

        bool hasBorder = borderThick > 0f && !borderPaint.IsTransparent();
        bool hasFill = fillHalfThick > 0f && !fillPaint.IsTransparent();

        if (!hasBorder && !hasFill) return;

        Vector2 size = Vector2.One * outerEdge * 2;
        borderPaint = transformPaint(borderPaint, center, -Vector2.One * outerEdge, 0f, size);
        var padding = Vector2.One * borderThickness;
        size -= padding * 2;
        fillPaint = transformPaint(fillPaint, center, -Vector2.One * outerEdge + padding, 0f, size);

        Vector2 startCapCenter = center + new Vector2(float.Cos(startAngle), float.Sin(startAngle)) * midRadius;
        Vector2 endCapCenter = center + new Vector2(float.Cos(endAngle), float.Sin(endAngle)) * midRadius;

        int capSegments = computeSegments(outerRadius / 2, float.Pi, quality);

        if (hasBorder) {
            addRingSegment(center, midRadius + fillHalfThick, midRadius + halfThick, startAngle, endAngle, borderPaint, segments);
            addRingSegment(center, midRadius - halfThick, midRadius - fillHalfThick, startAngle, endAngle, borderPaint, segments);
            addRingSegment(startCapCenter, fillHalfThick, halfThick, startAngle + float.Pi, startAngle + float.Tau, borderPaint, capSegments);
            addRingSegment(endCapCenter, fillHalfThick, halfThick, endAngle, endAngle + float.Pi, borderPaint, capSegments);
        }
        if (hasFill) {
            addRingSegment(center, midRadius - fillHalfThick, midRadius + fillHalfThick, startAngle, endAngle, fillPaint, segments);
            addRingSegment(startCapCenter, 0f, fillHalfThick, startAngle + float.Pi, startAngle + float.Tau, fillPaint, capSegments);
            addRingSegment(endCapCenter, 0f, fillHalfThick, endAngle, endAngle + float.Pi, fillPaint, capSegments);
        }

        if (aaSize == 0f) return;
        aaSize /= CameraZoom;

        if (hasBorder) {
            addCircleFringe(center, midRadius + halfThick, startAngle, endAngle, borderPaint, segments, true, aaSize);
            addCircleFringe(center, midRadius - halfThick, startAngle, endAngle, borderPaint, segments, false, aaSize);
            addCircleFringe(center, midRadius - fillHalfThick, startAngle, endAngle, borderPaint, segments, true, aaSize);
            addCircleFringe(center, midRadius + fillHalfThick, startAngle, endAngle, borderPaint, segments, false, aaSize);

            float arcSpan = endAngle - startAngle;

            float inSpan = arcSpan > float.Pi ? (arcSpanAngle(startCapCenter, endCapCenter, fillHalfThick) ?? 0) / 2 : 0;
            if (inSpan != float.Pi) {
                addCircleFringe(startCapCenter, fillHalfThick, startAngle + float.Pi, startAngle + float.Pi * 1.5f - inSpan, borderPaint, capSegments, false, aaSize);
                addCircleFringe(startCapCenter, fillHalfThick, startAngle + float.Pi * 1.5f + inSpan, startAngle + float.Tau, borderPaint, capSegments, false, aaSize);
                addCircleFringe(endCapCenter, fillHalfThick, endAngle, endAngle + float.Pi / 2 - inSpan, borderPaint, capSegments, false, aaSize);
                addCircleFringe(endCapCenter, fillHalfThick, endAngle + float.Pi, endAngle + float.Pi / 2 + inSpan, borderPaint, capSegments, false, aaSize);
            }

            float outSpan = arcSpan > float.Pi ? (arcSpanAngle(startCapCenter, endCapCenter, halfThick) ?? 0) / 2 : 0;
            if (outSpan != float.Pi) {
                halfThick -= 0.5f;
                addCircleFringe(startCapCenter, halfThick, startAngle + float.Pi, startAngle + float.Pi * 1.5f - outSpan, borderPaint, capSegments, true, aaSize);
                addCircleFringe(startCapCenter, halfThick, startAngle + float.Pi * 1.5f + outSpan, startAngle + float.Tau, borderPaint, capSegments, true, aaSize);
                addCircleFringe(endCapCenter, halfThick, endAngle, endAngle + float.Pi / 2 - outSpan, borderPaint, capSegments, true, aaSize);
                addCircleFringe(endCapCenter, halfThick, endAngle + float.Pi, endAngle + float.Pi / 2 + outSpan, borderPaint, capSegments, true, aaSize);
            }
        }
        if (hasFill) {
            float borderA = hasBorder ? byte.Max(borderPaint.ColorA.A, borderPaint.ColorB.A) / 255f : 0;
            var scaledFill = fillPaint * (1 - borderA);
            addCircleFringe(center, midRadius + fillHalfThick, startAngle, endAngle, scaledFill, segments, true, aaSize);
            addCircleFringe(center, midRadius - fillHalfThick, startAngle, endAngle, scaledFill, segments, false, aaSize);
            addCircleFringe(startCapCenter, fillHalfThick, startAngle + float.Pi, startAngle + float.Tau, scaledFill, capSegments, true, aaSize);
            addCircleFringe(endCapCenter, fillHalfThick, endAngle, endAngle + float.Pi, scaledFill, capSegments, true, aaSize);
        }
    }

    public void FillArc(Vector2 center, Paint fillPaint, float innerRadius, float outerRadius, float startAngle, float endAngle, ArcQuality quality = ArcQuality.Normal, float aaSize = 1f)
        => DrawArc(center, fillPaint, default, innerRadius, outerRadius, startAngle, endAngle, 0f, quality, aaSize);

    public void BorderArc(Vector2 center, Paint borderPaint, float innerRadius, float outerRadius, float startAngle, float endAngle, float borderThickness, ArcQuality quality = ArcQuality.Normal, float aaSize = 1f)
        => DrawArc(center, default, borderPaint, innerRadius, outerRadius, startAngle, endAngle, borderThickness, quality, aaSize);

    public void DrawCircle(Vector2 center, Paint fillPaint, Paint borderPaint, float radius, float borderThickness, ArcQuality quality = ArcQuality.Normal, float aaSize = 1f) {
        ensureBegun();
        var segments = computeSegments(radius, quality: quality);
        float innerRadius = float.Max(0, radius - borderThickness);

        bool hasBorder = borderThickness > 0 && !borderPaint.IsTransparent();
        bool hasFill = innerRadius > 0 && !fillPaint.IsTransparent();

        Vector2 size = Vector2.One * radius * 2;
        if (hasBorder) {
            borderPaint = transformPaint(borderPaint, center, -Vector2.One * radius, 0f, size);
            addRingSegment(center, innerRadius, radius, 0, float.Tau, borderPaint, segments);
        }
        if (innerRadius > 0) {
            var padding = Vector2.One * borderThickness;
            size -= padding * 2;
            fillPaint = transformPaint(fillPaint, center, -Vector2.One * radius + padding, 0f, size);
            addRingSegment(center, 0, innerRadius, 0, float.Tau, fillPaint, segments);
        }

        if (aaSize == 0f) return;
        aaSize /= CameraZoom;

        if (hasBorder) {
            addCircleFringe(center, radius, 0, float.Tau, borderPaint, segments, true, aaSize);
            addCircleFringe(center, innerRadius, 0, float.Tau, borderPaint, segments, false, aaSize);
        }
        if (hasFill) {
            float borderA = hasBorder ? byte.Max(borderPaint.ColorA.A, borderPaint.ColorB.A) / 255f : 0;
            addCircleFringe(center, innerRadius, 0, float.Tau, fillPaint * (1 - borderA), segments, true, aaSize);
        }
    }

    public void FillCircle(Vector2 center, Paint fillPaint, float radius, ArcQuality quality = ArcQuality.Normal, float aaSize = 1f)
        => DrawCircle(center, fillPaint, default, radius, 0f, quality, aaSize);

    public void BorderCircle(Vector2 center, Paint borderPaint, float radius, float borderThickness, ArcQuality quality = ArcQuality.Normal, float aaSize = 1f)
        => DrawCircle(center, default, borderPaint, radius, borderThickness, quality, aaSize);

    public void DrawNGon(Vector2 center, float radius, Paint fillPaint, Paint borderPaint, int sides, float borderThickness, float rotation = 0f, float aaSize = 1f) {
        ensureBegun();
        if (sides < 3 || radius <= 0f) return;

        float innerRadius = float.Max(0, radius - borderThickness);
        bool hasBorder = borderThickness > 0 && !borderPaint.IsTransparent();
        bool hasFill = innerRadius > 0 && !fillPaint.IsTransparent();

        if (!hasBorder && !hasFill) return;

        Vector2 size = Vector2.One * radius * 2;
        if (hasBorder) {
            borderPaint = transformPaint(borderPaint, center, -Vector2.One * radius, rotation, size);
            addRingSegment(center, innerRadius, radius, rotation, rotation + float.Tau, borderPaint, sides);
        }
        if (hasFill) {
            var padding = Vector2.One * borderThickness;
            size -= padding * 2;
            fillPaint = transformPaint(fillPaint, center, -Vector2.One * radius + padding, rotation, size);
            addRingSegment(center, 0, innerRadius, rotation, rotation + float.Tau, fillPaint, sides);
        }

        if (aaSize == 0f) return;
        aaSize /= CameraZoom;

        if (hasBorder) {
            addCircleFringe(center, radius, rotation, rotation + float.Tau, borderPaint, sides, true, aaSize);
            addCircleFringe(center, innerRadius, rotation, rotation + float.Tau, borderPaint, sides, false, aaSize);
        }
        if (hasFill) {
            float borderA = hasBorder ? byte.Max(borderPaint.ColorA.A, borderPaint.ColorB.A) / 255f : 0;
            addCircleFringe(center, innerRadius, rotation, rotation + float.Tau, fillPaint * (1 - borderA), sides, true, aaSize);
        }
    }

    public void FillNGon(Vector2 center, float radius, Paint fillPaint, int sides, float rotation = 0f, float aaSize = 1f)
        => DrawNGon(center, radius, fillPaint, default, sides, 0f, rotation, aaSize);

    public void BorderNGon(Vector2 center, float radius, Paint borderPaint, int sides, float borderThickness, float rotation = 0f, float aaSize = 1f)
        => DrawNGon(center, radius, default, borderPaint, sides, borderThickness, rotation, aaSize);

    public void DrawEllipse(Vector2 position, Vector2 size, Paint fillPaint, Paint borderPaint, float borderThickness, float rotation = 0f, Vector2 origin = default, ArcQuality quality = ArcQuality.Normal, float aaSize = 1f) {
        ensureBegun();
        if (size.X <= 0 || size.Y <= 0) return;

        float rx = size.X * 0.5f;
        float ry = size.Y * 0.5f;
        float minHalf = float.Min(rx, ry);
        borderThickness = float.Clamp(borderThickness, 0f, minHalf);

        borderPaint = transformPaint(borderPaint, position + origin, -origin, rotation, size);
        var padding = Vector2.One * borderThickness;
        fillPaint = transformPaint(fillPaint, position + padding + origin, -origin, rotation, size - padding * 2);

        bool hasFill = borderThickness < minHalf && !fillPaint.IsTransparent();
        bool hasBorder = borderThickness > 0f && !borderPaint.IsTransparent();

        if (!hasFill && !hasBorder) return;

        int segments = computeSegments(float.Max(rx, ry), float.Tau, quality);

        float innerRx = float.Max(0f, rx - borderThickness);
        float innerRy = float.Max(0f, ry - borderThickness);

        Vector2 center = position + new Vector2(rx, ry);
        Vector2 pivot = position + origin;
        bool hasRotation = rotation != 0f;
        float rotSin = 0f, rotCos = 1f;
        if (hasRotation) (rotSin, rotCos) = float.SinCos(rotation);

        Vector2 Rotate(Vector2 p) {
            if (!hasRotation) return p;
            float dx = p.X - pivot.X, dy = p.Y - pivot.Y;
            return new Vector2(
                pivot.X + dx * rotCos - dy * rotSin,
                pivot.Y + dx * rotSin + dy * rotCos);
        }

        PrimitiveVertex Vert(Vector2 p, Paint paint) => new(new Vector3(p, 0f), paint, _currentClip.Rect, _currentClip.Params);
        PrimitiveVertex VertTransparent(Vector2 p, Paint paint) => new(new Vector3(p, 0f), paint, _currentClip.Rect, _currentClip.Params) {
            ColorA = Color.Transparent,
            ColorB = Color.Transparent
        };

        if (hasFill) {
            ensureCapacity(segments + 2, segments * 3);
            int baseIdx = _vertexCount;

            _vertices[_vertexCount++] = Vert(Rotate(center), fillPaint);

            for (int i = 0; i <= segments; i++) {
                float angle = ((float)i / segments) * float.Tau;
                (float sin, float cos) = float.SinCos(angle);
                Vector2 pos = center + new Vector2(cos * innerRx, sin * innerRy);
                _vertices[_vertexCount++] = Vert(Rotate(pos), fillPaint);
            }

            for (int i = 0; i < segments; i++) {
                _indices[_indexCount++] = (short)baseIdx;
                _indices[_indexCount++] = (short)(baseIdx + 1 + i);
                _indices[_indexCount++] = (short)(baseIdx + 1 + i + 1);
            }
        }

        if (hasBorder) {
            int borderVerts = (segments + 1) * 2;
            ensureCapacity(borderVerts, segments * 6);
            int baseIdx = _vertexCount;

            for (int i = 0; i <= segments; i++) {
                float angle = ((float)i / segments) * float.Tau;
                (float sin, float cos) = float.SinCos(angle);
                Vector2 innerPos = center + new Vector2(cos * innerRx, sin * innerRy);
                Vector2 outerPos = center + new Vector2(cos * rx, sin * ry);

                _vertices[_vertexCount++] = Vert(Rotate(innerPos), borderPaint);
                _vertices[_vertexCount++] = Vert(Rotate(outerPos), borderPaint);
            }

            for (int i = 0; i < segments; i++) {
                int inner0 = baseIdx + i * 2;
                int outer0 = baseIdx + i * 2 + 1;
                int inner1 = baseIdx + (i + 1) * 2;
                int outer1 = baseIdx + (i + 1) * 2 + 1;

                _indices[_indexCount++] = (short)inner0;
                _indices[_indexCount++] = (short)outer0;
                _indices[_indexCount++] = (short)inner1;

                _indices[_indexCount++] = (short)outer0;
                _indices[_indexCount++] = (short)outer1;
                _indices[_indexCount++] = (short)inner1;
            }
        }

        if (aaSize == 0f) return;
        aaSize /= CameraZoom;

        void addEllipseFringe(float erx, float ery, Paint paint, bool outer, float borderAOffset = 0f) {
            if (segments < 1 || erx <= 0f || ery <= 0f) return;

            int fringeVerts = (segments + 1) * 2;
            ensureCapacity(fringeVerts, segments * 6);
            int fringeStart = _vertexCount;

            Paint fringePaint = paint;
            if (borderAOffset > 0f) {
                fringePaint = paint * (1f - borderAOffset);
            }

            for (int i = 0; i <= segments; i++) {
                float angle = ((float)i / segments) * float.Tau;
                (float sin, float cos) = float.SinCos(angle);

                Vector2 basePos = center + new Vector2(cos * erx, sin * ery);
                Vector2 normal = Vector2.Normalize(new Vector2(cos * ery, sin * erx));
                Vector2 fringePos = outer
                    ? basePos + normal * aaSize
                    : basePos - normal * aaSize;

                Vector2 rotatedBase = Rotate(basePos);
                Vector2 rotatedFringe;
                if (hasRotation) {
                    float rxNorm = normal.X * aaSize;
                    float ryNorm = normal.Y * aaSize;
                    float wdx = rxNorm * rotCos - ryNorm * rotSin;
                    float wdy = rxNorm * rotSin + ryNorm * rotCos;
                    rotatedFringe = outer
                        ? new Vector2(rotatedBase.X + wdx, rotatedBase.Y + wdy)
                        : new Vector2(rotatedBase.X - wdx, rotatedBase.Y - wdy);
                }
                else {
                    rotatedFringe = fringePos;
                }

                _vertices[_vertexCount++] = Vert(rotatedBase, fringePaint);
                _vertices[_vertexCount++] = VertTransparent(rotatedFringe, fringePaint);
            }

            for (int i = 0; i < segments; i++) {
                int v0 = fringeStart + i * 2;
                int v1 = fringeStart + i * 2 + 1;
                int v2 = fringeStart + (i + 1) * 2;
                int v3 = fringeStart + (i + 1) * 2 + 1;

                _indices[_indexCount++] = (short)v0;
                _indices[_indexCount++] = (short)v1;
                _indices[_indexCount++] = (short)v2;

                _indices[_indexCount++] = (short)v1;
                _indices[_indexCount++] = (short)v3;
                _indices[_indexCount++] = (short)v2;
            }
        }

        if (hasBorder) {
            addEllipseFringe(rx, ry, borderPaint, true);
            addEllipseFringe(innerRx, innerRy, borderPaint, false);
        }
        if (hasFill) {
            float borderA = hasBorder ? byte.Max(borderPaint.ColorA.A, borderPaint.ColorB.A) / 255f : 0f;
            addEllipseFringe(innerRx, innerRy, fillPaint, true, borderA);
        }
    }

    public void FillEllipse(Vector2 position, Vector2 size, Paint fillPaint, float rotation = 0f, Vector2 origin = default, ArcQuality quality = ArcQuality.Normal, float aaSize = 1f)
        => DrawEllipse(position, size, fillPaint, default, 0f, rotation, origin, quality, aaSize);

    public void BorderEllipse(Vector2 position, Vector2 size, Paint borderPaint, float borderThickness, float rotation = 0f, Vector2 origin = default, ArcQuality quality = ArcQuality.Normal, float aaSize = 1f)
        => DrawEllipse(position, size, default, borderPaint, borderThickness, rotation, origin, quality, aaSize);

    public void DrawEllipse(Vector2 center, float xRadius, float yRadius, Paint fillPaint, Paint borderPaint, float borderThickness, float rotation = 0f, ArcQuality quality = ArcQuality.Normal, float aaSize = 1f) {
        Vector2 size = new(xRadius * 2f, yRadius * 2f);
        Vector2 pos = center - new Vector2(xRadius, yRadius);
        Vector2 origin = new(xRadius, yRadius);
        DrawEllipse(pos, size, fillPaint, borderPaint, borderThickness, rotation, origin, quality, aaSize);
    }

    public void FillEllipse(Vector2 center, float xRadius, float yRadius, Paint fillPaint, float rotation = 0f, ArcQuality quality = ArcQuality.Normal, float aaSize = 1f)
        => DrawEllipse(center, xRadius, yRadius, fillPaint, default, 0f, rotation, quality, aaSize);

    public void BorderEllipse(Vector2 center, float xRadius, float yRadius, Paint borderPaint, float borderThickness, float rotation = 0f, ArcQuality quality = ArcQuality.Normal, float aaSize = 1f)
        => DrawEllipse(center, xRadius, yRadius, default, borderPaint, borderThickness, rotation, quality, aaSize);

    private static Paint transformPaint(Paint paint, Vector2 center, Vector2 offset, float rotation, Vector2? size = null) {
        if (paint.isNormalized && size.HasValue) {
            var r = size.Value;
            paint.Start = new Vector2(paint.Start.X * r.X, paint.Start.Y * r.Y);
            paint.End = new Vector2(paint.End.X * r.X, paint.End.Y * r.Y);
            paint.isNormalized = false;
            paint.IsLocal = true;
        }

        if (paint.isPixelOffsets) {
            float distance = Vector2.Distance(paint.Start, paint.End);
            if (distance > 0.0001f)
                (paint.OffsetA, paint.OffsetB) = (paint.OffsetA / distance, (distance - paint.OffsetB) / distance);
            else
                (paint.OffsetA, paint.OffsetB) = (0f, 1f);
            paint.isPixelOffsets = false;
        }

        if (!paint.IsLocal || paint.Type == Paint.PaintType.Solid)
            return paint;

        Vector2 p1 = new Vector2(paint.Start.X, paint.Start.Y) + offset;
        Vector2 p2 = new Vector2(paint.End.X, paint.End.Y) + offset;

        if (rotation != 0f) {
            var (sin, cos) = float.SinCos(rotation);
            Vector2 r1 = new(p1.X * cos - p1.Y * sin, p1.X * sin + p1.Y * cos);
            Vector2 r2 = new(p2.X * cos - p2.Y * sin, p2.X * sin + p2.Y * cos);
            p1 = r1;
            p2 = r2;
        }

        p1 += center;
        p2 += center;

        paint.Start = new Vector2(p1.X, p1.Y);
        paint.End = new Vector2(p2.X, p2.Y);
        return paint;
    }
    private static float? arcSpanAngle(Vector2 c1, Vector2 c2, float r) {
        float d = Vector2.Distance(c1, c2);
        if (d >= 2f * r) return null;
        if (d < float.Epsilon) return float.Pi;
        return 2f * float.Acos(d / (2f * r));
    }
    private void addTextureFringe(Span<Vector2> centers, float radius, int cornerSegments, Paint paint, bool hasRotation, float rotSin, float rotCos, Vector2 pivot, int texIndex, Vector2 position, Vector2 actualSize, Vector2 uvMin, Vector2 uvMax, bool flipH, bool flipV, float aaSize) {
        int perimeterVerts = (cornerSegments + 1) * 4;
        ensureCapacity(perimeterVerts * 2, perimeterVerts * 6);

        float step = MathHelper.PiOver2 / cornerSegments;
        int fringeStart = _vertexCount;

        for (int c = 0; c < 4; c++) {
            float startAngle = c * MathHelper.PiOver2;
            for (int i = 0; i <= cornerSegments; i++) {
                float angle = startAngle + i * step;
                (float sin, float cos) = float.SinCos(angle);
                Vector2 dir = new(cos, sin);
                Vector2 basePos = centers[c] + dir * radius;
                Vector2 fringePos = basePos + dir * aaSize;

                Vector2 worldBase, worldFringe;
                if (hasRotation) {
                    float rx = basePos.X - pivot.X, ry = basePos.Y - pivot.Y;
                    worldBase = new Vector2(pivot.X + rx * rotCos - ry * rotSin, pivot.Y + rx * rotSin + ry * rotCos);

                    float wdx = (dir.X * rotCos - dir.Y * rotSin) * aaSize;
                    float wdy = (dir.X * rotSin + dir.Y * rotCos) * aaSize;
                    worldFringe = new Vector2(worldBase.X + wdx, worldBase.Y + wdy);
                }
                else {
                    worldBase = basePos;
                    worldFringe = fringePos;
                }

                Vector2 getUV(Vector2 p) {
                    float tx = (p.X - position.X) / actualSize.X;
                    float ty = (p.Y - position.Y) / actualSize.Y;

                    if (flipH) tx = 1f - tx;
                    if (flipV) ty = 1f - ty;

                    return new Vector2(
                        float.Lerp(uvMin.X, uvMax.X, tx),
                        float.Lerp(uvMin.Y, uvMax.Y, ty)
                    );
                }

                _vertices[_vertexCount++] = new PrimitiveVertex(new Vector3(worldBase, 0), getUV(basePos), texIndex, paint, _currentClip.Rect, _currentClip.Params);

                _vertices[_vertexCount++] = new PrimitiveVertex(new Vector3(worldFringe, 0), getUV(basePos), texIndex, paint, _currentClip.Rect, _currentClip.Params) {
                    ColorA = Color.Transparent,
                    ColorB = Color.Transparent
                };
            }
        }

        for (int k = 0; k < perimeterVerts; k++) {
            int next = (k + 1) % perimeterVerts;
            int v0 = fringeStart + k * 2;
            int v1 = fringeStart + k * 2 + 1;
            int v2 = fringeStart + next * 2;
            int v3 = fringeStart + next * 2 + 1;

            _indices[_indexCount++] = (short)v0;
            _indices[_indexCount++] = (short)v1;
            _indices[_indexCount++] = (short)v2;

            _indices[_indexCount++] = (short)v1;
            _indices[_indexCount++] = (short)v3;
            _indices[_indexCount++] = (short)v2;
        }
    }
    public void DrawTexture(Texture2D texture, Vector2 position, Vector2? size = null, RectangleF? sourceRect = null, Paint? tint = null, float rotation = 0f, Vector2? origin = null, SpriteEffects effects = SpriteEffects.None, float rounding = 0f, ArcQuality cornerQuality = ArcQuality.Normal, float aaSize = 1f) {
        ensureBegun();
        Vector2 actualSize = size ?? new Vector2(texture.Width, texture.Height);
        if (actualSize.X <= 0 || actualSize.Y <= 0) return;

        Paint actualTint = tint ?? Paint.Solid(Color.White);
        if (actualTint.IsTransparent()) return;
        var usedOrigin = origin ?? actualSize / 2;
        actualTint = transformPaint(actualTint, position + usedOrigin, -usedOrigin, rotation, size);

        float minHalf = float.Min(actualSize.X, actualSize.Y) * 0.5f;
        rounding = float.Clamp(rounding, 0, minHalf);

        var cornerSegments = rounding > 0 ? computeSegments(rounding, MathHelper.PiOver2, cornerQuality) : 1;

        int perimeterVerts = (cornerSegments + 1) * 4;
        ensureCapacity(perimeterVerts + 1, perimeterVerts * 3);
        int texIndex = getTextureIndex(texture);

        float outR = rounding;
        Span<Vector2> outCenters = [
            position + new Vector2(actualSize.X - outR, actualSize.Y - outR),
            position + new Vector2(outR, actualSize.Y - outR),
            position + new Vector2(outR, outR),
            position + new Vector2(actualSize.X - outR, outR),
        ];
        Span<float> startAngles = [0, MathHelper.PiOver2, float.Pi, float.Pi * 1.5f];
        float step = MathHelper.PiOver2 / cornerSegments;

        float rotSin = 0, rotCos = 1;
        bool hasRotation = rotation != 0f;
        if (hasRotation) {
            rotSin = float.Sin(rotation);
            rotCos = float.Cos(rotation);
        }

        Vector2 transform(Vector2 p) {
            if (!hasRotation) return p;
            float rx = p.X - (position.X + usedOrigin.X);
            float ry = p.Y - (position.Y + usedOrigin.Y);
            return new Vector2(
                position.X + usedOrigin.X + rx * rotCos - ry * rotSin,
                position.Y + usedOrigin.Y + rx * rotSin + ry * rotCos
            );
        }

        RectangleF src = sourceRect.HasValue ?
            new RectangleF(sourceRect.Value.X, sourceRect.Value.Y, sourceRect.Value.Width, sourceRect.Value.Height) :
            new RectangleF(0, 0, texture.Width, texture.Height);

        Vector2 uvMin = new(src.X / texture.Width, src.Y / texture.Height);
        Vector2 uvMax = new((src.X + src.Width) / texture.Width, (src.Y + src.Height) / texture.Height);

        bool flipH = (effects & SpriteEffects.FlipHorizontally) != 0;
        bool flipV = (effects & SpriteEffects.FlipVertically) != 0;

        Vector2 getUV(Vector2 p) {
            float tx = (p.X - position.X) / actualSize.X;
            float ty = (p.Y - position.Y) / actualSize.Y;

            if (flipH) tx = 1f - tx;
            if (flipV) ty = 1f - ty;

            return new Vector2(
                float.Lerp(uvMin.X, uvMax.X, tx),
                float.Lerp(uvMin.Y, uvMax.Y, ty)
            );
        }

        int startIdx = _vertexCount;
        Vector2 centerPos = position + actualSize * 0.5f;

        _vertices[_vertexCount++] = new PrimitiveVertex(
            new Vector3(transform(centerPos), 0f),
            getUV(centerPos), texIndex,
            actualTint, _currentClip.Rect, _currentClip.Params);

        int vertCounter = 0;
        for (int c = 0; c < 4; c++) {
            for (int i = 0; i <= cornerSegments; i++) {
                float angle = startAngles[c] + i * step;
                (float sin, float cos) = float.SinCos(angle);
                Vector2 pos = outCenters[c] + new Vector2(cos, sin) * outR;

                _vertices[_vertexCount++] = new PrimitiveVertex(
                    new Vector3(transform(pos), 0f),
                    getUV(pos), texIndex,
                    actualTint, _currentClip.Rect, _currentClip.Params);

                _indices[_indexCount++] = (short)startIdx;
                _indices[_indexCount++] = (short)(startIdx + vertCounter + 1);
                _indices[_indexCount++] = (short)(startIdx + (vertCounter + 1) % perimeterVerts + 1);
                vertCounter++;
            }
        }

        if (aaSize != 0f) {
            aaSize /= CameraZoom;
            Vector2 aaPivot = position + usedOrigin;
            addTextureFringe(outCenters, outR, cornerSegments, actualTint, hasRotation, rotSin, rotCos, aaPivot, texIndex, position, actualSize, uvMin, uvMax, flipH, flipV, aaSize);
        }
    }

    public void DrawTexture(Texture2D texture, Vector2 position, Paint paint, float rounding = 0f, ArcQuality cornerQuality = ArcQuality.Normal, float aaSize = 1f) {
        DrawTexture(texture, position, null, null, paint, 0f, default, SpriteEffects.None, rounding, cornerQuality, aaSize);
    }

    public void DrawTexture(Texture2D texture, RectangleF destinationRectangle, Paint paint, float rounding = 0f, ArcQuality cornerQuality = ArcQuality.Normal, float aaSize = 1f) {
        DrawTexture(texture, new Vector2(destinationRectangle.X, destinationRectangle.Y), new Vector2(destinationRectangle.Width, destinationRectangle.Height), null, paint, 0f, default, SpriteEffects.None, rounding, cornerQuality, aaSize);
    }

    public void DrawTexture(Texture2D texture, Vector2 position, RectangleF? sourceRectangle, Paint paint, float rounding = 0f, ArcQuality cornerQuality = ArcQuality.Normal, float aaSize = 1f) {
        Vector2 size = sourceRectangle.HasValue ? new Vector2(sourceRectangle.Value.Width, sourceRectangle.Value.Height) : new Vector2(texture.Width, texture.Height);
        DrawTexture(texture, position, size, sourceRectangle, paint, 0f, default, SpriteEffects.None, rounding, cornerQuality, aaSize);
    }

    public void DrawTexture(Texture2D texture, RectangleF destinationRectangle, RectangleF? sourceRectangle, Paint paint, float rounding = 0f, ArcQuality cornerQuality = ArcQuality.Normal, float aaSize = 1f) {
        DrawTexture(texture, new Vector2(destinationRectangle.X, destinationRectangle.Y), new Vector2(destinationRectangle.Width, destinationRectangle.Height), sourceRectangle, paint, 0f, default, SpriteEffects.None, rounding, cornerQuality, aaSize);
    }

    public void DrawTexture(Texture2D texture, Vector2 position, RectangleF? sourceRectangle, Paint paint, float rotation, Vector2 origin, float scale, SpriteEffects effects, float rounding = 0f, ArcQuality cornerQuality = ArcQuality.Normal, float aaSize = 1f) {
        Vector2 srcSize = sourceRectangle.HasValue ? new Vector2(sourceRectangle.Value.Width, sourceRectangle.Value.Height) : new Vector2(texture.Width, texture.Height);
        DrawTexture(texture, position, srcSize * scale, sourceRectangle, paint, rotation, origin * scale, effects, rounding, cornerQuality, aaSize);
    }

    public void DrawTexture(Texture2D texture, Vector2 position, RectangleF? sourceRectangle, Paint paint, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float rounding = 0f, ArcQuality cornerQuality = ArcQuality.Normal, float aaSize = 1f) {
        Vector2 srcSize = sourceRectangle.HasValue ? new Vector2(sourceRectangle.Value.Width, sourceRectangle.Value.Height) : new Vector2(texture.Width, texture.Height);
        DrawTexture(texture, position, srcSize * scale, sourceRectangle, paint, rotation, origin * scale, effects, rounding, cornerQuality, aaSize);
    }

    public void DrawTexture(Texture2D texture, RectangleF destinationRectangle, RectangleF? sourceRectangle, Paint paint, float rotation, Vector2 origin, SpriteEffects effects, float rounding = 0f, ArcQuality cornerQuality = ArcQuality.Normal, float aaSize = 1f) {
        Vector2 srcSize = sourceRectangle.HasValue ? new Vector2(sourceRectangle.Value.Width, sourceRectangle.Value.Height) : new Vector2(texture.Width, texture.Height);
        Vector2 destSize = new(destinationRectangle.Width, destinationRectangle.Height);
        Vector2 scale = new(destSize.X / srcSize.X, destSize.Y / srcSize.Y);

        DrawTexture(texture, new Vector2(destinationRectangle.X, destinationRectangle.Y), destSize, sourceRectangle, paint, rotation, origin * scale, effects, rounding, cornerQuality, aaSize);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PrimitiveVertex : IVertexType {
        public Vector3 Position;
        public Vector4 ClipRect;
        public Vector2 ClipParams;
        public Color ColorA;
        public Color ColorB;
        public Vector4 TexCoords;
        public Vector4 GradientCoords;
        public Vector3 PaintParams;

        public static readonly VertexDeclaration VertexDeclaration = new(
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 0),
            new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1),
            new VertexElement(36, VertexElementFormat.Color, VertexElementUsage.Color, 0),
            new VertexElement(40, VertexElementFormat.Color, VertexElementUsage.Color, 1),
            new VertexElement(44, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2),
            new VertexElement(60, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3),
            new VertexElement(76, VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 4)
        );

        public PrimitiveVertex(in Vector3 pos, in Paint paint, in Vector4 clipRect, in Vector2 clipParams) {
            Position = pos;
            ClipRect = clipRect;
            ClipParams = clipParams;
            ColorA = paint.ColorA;
            ColorB = paint.ColorB;
            TexCoords = -Vector4.UnitZ;
            GradientCoords = new Vector4(paint.Start.X, paint.Start.Y, paint.End.X, paint.End.Y);
            float safePower = float.Clamp(paint.EasingPower, 0f, 99.9f);
            float packedData = ((float)paint.Type * 1000f) + ((float)paint.Easing * 100f) + safePower;
            PaintParams = new Vector3(paint.OffsetA, paint.OffsetB, packedData);
        }

        public PrimitiveVertex(in Vector3 pos, in Vector2 texCoords, int index, in Paint paint, in Vector4 clipRect, in Vector2 clipParams) {
            Position = pos;
            ClipRect = clipRect;
            ClipParams = clipParams;
            ColorA = paint.ColorA;
            ColorB = paint.ColorB;
            TexCoords = new Vector4(texCoords.X, texCoords.Y, index, 0);
            GradientCoords = new Vector4(paint.Start.X, paint.Start.Y, paint.End.X, paint.End.Y);
            float safePower = float.Clamp(paint.EasingPower, 0f, 99.9f);
            float packedData = ((float)paint.Type * 1000f) + ((float)paint.Easing * 100f) + safePower;
            PaintParams = new Vector3(paint.OffsetA, paint.OffsetB, packedData);
        }

        readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
    }
    private static float qualityToError(ArcQuality quality) => quality switch {
        ArcQuality.Draft => 1.0f,
        ArcQuality.Normal => 0.125f,
        ArcQuality.HiDef => 0.0625f,
        _ => 0.5f
    };

    public int computeSegments(float radius, float angleSpanRadians = float.Tau, ArcQuality quality = ArcQuality.Normal, int minSegments = 3) {
        var pixelRadius = radius * CameraZoom;
        if (pixelRadius <= 0f) return minSegments;

        float clampedError = float.Min(qualityToError(quality), pixelRadius);
        int segments = (int)float.Ceiling(float.Pi / float.Acos(1.0f - clampedError / pixelRadius) * (float.Abs(angleSpanRadians) / float.Tau));

        return int.Max(segments, minSegments);
    }
}

public enum ArcQuality { Draft, Normal, HiDef }