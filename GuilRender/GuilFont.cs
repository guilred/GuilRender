using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using Guilred.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Guilred.Rendering;

public sealed class GuilFont : IDisposable {
    private class AtlasData {
        public Dictionary<char, (int x, int y, int w)> CharsData = [];
        public int Size;
    }
    private readonly List<AtlasData> _atlases = [];
    public Texture2D MegaAtlas { get; private set; }
    public float Spacing { get; set; } = 5;
    public float LineSpacing { get; set; } = 5;

    public GuilFont(GraphicsDevice graphics, GuilBatch batch, string guifFilePath) {
        using var archive = ZipFile.OpenRead(guifFilePath);

        string? readEntryText(string entryName) {
            var entry = archive.GetEntry(entryName);
            if (entry == null) return null;
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        Texture2D? readEntryTexture(string entryName) {
            var entry = archive.GetEntry(entryName);
            if (entry == null) return null;
            using var entryStream = entry.Open();
            using var memStream = new MemoryStream();
            entryStream.CopyTo(memStream);
            memStream.Position = 0;
            return Texture2D.FromStream(graphics, memStream);
        }

        var metadataText = readEntryText("metadata")
            ?? throw new Exception("Font file is missing metadata.");

        var sizes = metadataText.Replace("sizes:", "").Trim()
            .Split(',').Select(s => int.Parse(s.Trim())).OrderBy(s => s);

        List<(int stripH, char c, int sx, int w, Texture2D stripTex)> glyphsToPack = [];
        List<Texture2D> stripTextures = [];

        foreach (var nominalSize in sizes) {
            var stripTex = readEntryTexture($"atlas_{nominalSize}");
            var charsDataText = readEntryText($"chars_data_{nominalSize}");
            if (stripTex == null || charsDataText == null) continue;

            stripTextures.Add(stripTex);

            var charIntPairs = charsDataText.Split('\n');
            foreach (var cip in charIntPairs) {
                if (string.IsNullOrWhiteSpace(cip)) continue;
                var parts = cip.Split(' ');
                var c = parts[0][0];
                var sx = int.Parse(parts[1]);
                var w = int.Parse(parts[2]);
                glyphsToPack.Add((stripTex.Height, c, sx, w, stripTex));
            }
        }

        if (glyphsToPack.Count == 0) throw new Exception("No valid atlases found.");

        glyphsToPack.Sort((a, b) => b.stripH.CompareTo(a.stripH));

        var padding = 2;
        var currentX = padding;
        var currentY = padding;
        var currentRowHeight = 0;
        var megaWidth = 2048;

        List<(int stripH, char c, int x, int y, int w, Texture2D stripTex, int sx)> packedGlyphs = [];

        foreach (var g in glyphsToPack) {
            if (currentX + g.w + padding > megaWidth) {
                currentX = padding;
                currentY += currentRowHeight + padding;
                currentRowHeight = 0;
            }

            packedGlyphs.Add((g.stripH, g.c, currentX, currentY, g.w, g.stripTex, g.sx));
            currentX += g.w + padding;
            currentRowHeight = int.Max(currentRowHeight, g.stripH);
        }

        var megaHeight = currentY + currentRowHeight + padding;

        var renderTarget = new RenderTarget2D(graphics, megaWidth, megaHeight);
        graphics.SetRenderTarget(renderTarget);
        graphics.Clear(Color.Transparent);
        batch.Begin(blendState: BlendState.NonPremultiplied);
        foreach (var g in packedGlyphs)
            batch.DrawTexture(g.stripTex, new Vector2(g.x, g.y), new Rectangle(g.sx, 0, g.w, g.stripH), Color.White, aaSize: 0);
        batch.End();
        graphics.SetRenderTarget(null);
        MegaAtlas = renderTarget;

        foreach (var tex in stripTextures) tex.Dispose();

        var grouped = packedGlyphs.GroupBy(g => g.stripH).OrderBy(g => g.Key);
        foreach (var group in grouped) {
            var atlasData = new AtlasData { Size = group.Key };
            foreach (var g in group) {
                atlasData.CharsData[g.c] = (g.x, g.y, g.w);
            }
            _atlases.Add(atlasData);
        }
    }
    public float GetSpacing(float height) {
        return Spacing * height / 120f;
    }

    private AtlasData getBestAtlas(float targetHeight) {
        var left = 0;
        var right = _atlases.Count - 1;
        if (targetHeight <= _atlases[0].Size) return _atlases[0];
        if (targetHeight >= _atlases[right].Size) return _atlases[right];

        while (left <= right) {
            var mid = left + (right - left) / 2;
            var atlasSize = _atlases[mid].Size;

            if (atlasSize == targetHeight) return _atlases[mid];
            if (atlasSize < targetHeight) left = mid + 1;
            else right = mid - 1;
        }

        if (left >= _atlases.Count) return _atlases[right];
        if (right < 0) return _atlases[left];

        var distLeft = float.Abs(_atlases[left].Size - targetHeight);
        var distRight = float.Abs(_atlases[right].Size - targetHeight);
        return distLeft <= distRight ? _atlases[left] : _atlases[right];
    }

    private readonly struct FontContext {
        public readonly AtlasData Atlas;
        public readonly AtlasData LargestAtlas;
        public readonly float AtlasScale;
        public readonly float LAtlasScale;
        public readonly float Spacing;
        public readonly float LineSpacing;
        public readonly float SpacingScale;

        public FontContext(GuilFont font, float height, float? spacing, float? lineSpacing, GuilBatch? batch = null) {
            (Atlas, LargestAtlas) = (font.getBestAtlas(height * (batch?.CameraZoom ?? 1)), font._atlases[^1]);
            (SpacingScale, AtlasScale, LAtlasScale) = (height / 120f, height / Atlas.Size, height / LargestAtlas.Size);
            (Spacing, LineSpacing) = ((spacing ?? font.Spacing) * SpacingScale, lineSpacing ?? font.LineSpacing);
        }
    }

    private static float getCharWidth(char c, in FontContext ctx) {
        if (c == ' ') {
            return ctx.LargestAtlas.CharsData.TryGetValue('$', out var dlr)
                ? dlr.w * ctx.LAtlasScale + ctx.Spacing
                : ctx.Spacing * 2;
        }
        if (c == '\t') {
            return ctx.LargestAtlas.CharsData.TryGetValue('$', out var dlr)
                ? 4 * dlr.w * ctx.LAtlasScale + ctx.Spacing
                : ctx.Spacing * 5;
        }
        if (ctx.LargestAtlas.CharsData.TryGetValue(c, out var offset) || ctx.LargestAtlas.CharsData.TryGetValue('?', out offset)) {
            return offset.w * ctx.LAtlasScale + ctx.Spacing;
        }
        return 0;
    }
    public Vector2 MeasureString(List<char> text, float height, float? spacing = null, float? lineSpacing = null, int? index = null, int? length = null) {
        return MeasureString(CollectionsMarshal.AsSpan(text), height, spacing, lineSpacing, index, length);
    }
    public Vector2 MeasureString(string text, float height, float? spacing = null, float? lineSpacing = null, int? index = null, int? length = null) {
        return MeasureString(text.AsSpan(), height, spacing, lineSpacing, index, length);
    }
    public Vector2 MeasureString(ReadOnlySpan<char> text, float height, float? spacing = null, float? lineSpacing = null, int? index = null, int? length = null) {
        if (text.Length == 0) return Vector2.Zero;
        var ctx = new FontContext(this, height, spacing, lineSpacing);
        var size = Vector2.UnitY * height;
        var currWidth = 0f;

        var slice = text.Slice(index ?? 0, length ?? (text.Length - (index ?? 0)));

        for (var i = 0; i < slice.Length; i++) {
            var c = slice[i];
            if (c == '\n') {
                size.Y += height + ctx.LineSpacing;
                size.X = float.Max(size.X, currWidth - ctx.Spacing);
                currWidth = 0;
                continue;
            }
            currWidth += getCharWidth(c, in ctx);
        }
        size.X = float.Max(size.X, currWidth - ctx.Spacing);
        return size;
    }
    public Vector2 MeasureString(List<List<char>> lines, float height, float? spacing = null, float? lineSpacing = null) {
        if (lines.Count == 0) return Vector2.Zero;
        var currLS = lineSpacing ?? LineSpacing;
        var size = Vector2.UnitY * (height * lines.Count + currLS * (lines.Count - 1));
        for (int i = 0; i < lines.Count; i++) {
            var line = lines[i];
            size.X = float.Max(size.X, MeasureString(line, height, spacing, currLS).X);
        }
        return size;
    }
    public void DrawString(GuilBatch batch, string text, Vector2 position, Paint paint, float height, float? spacing = null, float? lineSpacing = null, int? index = null, int? length = null, float rotation = 0, List<Paint>? perCharColor = null, Alignment alignment = default) {
        DrawString(batch, text.AsSpan(), position, paint, height, spacing, lineSpacing, index, length, rotation, perCharColor, alignment);
    }
    public void DrawString(GuilBatch batch, ReadOnlySpan<char> text, Vector2 position, Paint paint, float height, float? spacing = null, float? lineSpacing = null, int? index = null, int? length = null, float rotation = 0, List<Paint>? perCharColor = null, Alignment alignment = default) {
        if (text.Length == 0) return;
        var ctx = new FontContext(this, height, spacing, lineSpacing, batch);
        var slice = text.Slice(index ?? 0, length ?? text.Length);

        var totalSize = MeasureString(slice, height, spacing, lineSpacing);
        var currY = position.Y;
        if (alignment.yAlignment == YAlignment.Center) currY -= totalSize.Y / 2;
        else if (alignment.yAlignment == YAlignment.Bottom) currY -= totalSize.Y;
        if (alignment.xAlignment == XAlignment.Center) {
            position.X -= totalSize.X / 2;
        }
        else if (alignment.xAlignment == XAlignment.Right) {
            position.X -= totalSize.X;
        }

        var globalIndex = index ?? 0;

        while (!slice.IsEmpty) {
            var nl = slice.IndexOf('\n');
            var lineLength = nl < 0 ? slice.Length : nl;
            var line = slice[..lineLength];

            var lineWidth = MeasureString(line, height, spacing, lineSpacing).X;
            var currX = position.X;
            if (alignment.textAlignment == XAlignment.Center) currX += (totalSize.X - lineWidth) / 2;
            else if (alignment.textAlignment == XAlignment.Right) currX += totalSize.X - lineWidth;

            for (var i = 0; i < line.Length; i++) {
                var c = line[i];
                var charWidth = getCharWidth(c, in ctx);

                if (c == ' ' || c == '\t') {
                    currX += charWidth;
                    continue;
                }

                if (ctx.Atlas.CharsData.TryGetValue(c, out var offset) || ctx.Atlas.CharsData.TryGetValue('?', out offset)) {
                    Rectangle sourceRect = new(offset.x, offset.y, offset.w, ctx.Atlas.Size);
                    var drawPos = new Vector2(currX, currY);
                    if (rotation != 0) drawPos.RotateAround(position, rotation);

                    var charPaint = paint;
                    if (perCharColor is not null) charPaint = perCharColor[globalIndex + i];

                    batch.DrawTexture(MegaAtlas, drawPos, sourceRect, charPaint, rotation, Vector2.Zero, ctx.AtlasScale, SpriteEffects.None, aaSize: 0);
                    currX += charWidth;
                }
            }

            if (nl < 0) break;
            currY += height + ctx.LineSpacing;
            globalIndex += nl + 1;
            slice = slice[(nl + 1)..];
        }
    }
    private readonly List<int> _starts = new(256);
    private List<int> getVisualLineStarts(ReadOnlySpan<char> segment, float posX, float wrapX, in FontContext ctx) {
        _starts.Clear();
        _starts.Add(0);
        var currX = posX;

        for (var i = 0; i < segment.Length; i++) {
            var c = segment[i];
            if (c == '\n') continue;

            var charWidth = getCharWidth(c, in ctx);
            if (c == ' ' || c == '\t') { currX += charWidth; continue; }

            var prevChar = i > 0 ? segment[i - 1] : ' ';
            if (prevChar == ' ' || prevChar == '\n') {
                var wordWidth = 0f;
                for (var k = i; k < segment.Length; k++) {
                    var wc = segment[k];
                    if (wc == ' ' || wc == '\n') break;
                    wordWidth += getCharWidth(wc, in ctx);
                }
                if (currX != posX && currX + wordWidth > wrapX) {
                    currX = posX;
                    _starts.Add(i);
                }
            }
            if (currX != posX && currX + charWidth > wrapX) {
                currX = posX;
                _starts.Add(i);
            }
            currX += charWidth;
        }
        return _starts;
    }
    private static void measureWrappedSegment(ReadOnlySpan<char> segment, ref float currX, ref float currY, ref float maxX, Vector2 position, float height, float wrapX, in FontContext ctx) {
        for (var i = 0; i < segment.Length; i++) {
            var c = segment[i];
            if (c == '\n') continue;

            var charWidth = getCharWidth(c, in ctx);
            if (c == ' ' || c == '\t') {
                if (i < segment.Length) currX += charWidth;
                continue;
            }

            var prevChar = i > 0 ? segment[i - 1] : ' ';
            if (prevChar == ' ' || prevChar == '\n') {
                var wordWidth = 0f;
                for (var k = i; k < segment.Length; k++) {
                    var wc = segment[k];
                    if (wc == ' ' || wc == '\n') break;
                    wordWidth += getCharWidth(wc, in ctx);
                }
                if (currX != position.X && currX + wordWidth > wrapX) {
                    maxX = float.Max(maxX, currX);
                    currX = position.X;
                    currY += height + ctx.LineSpacing;
                }
            }
            if (currX != position.X && currX + charWidth > wrapX) {
                maxX = float.Max(maxX, currX);
                currX = position.X;
                currY += height + ctx.LineSpacing;
            }

            if (i >= segment.Length) {
                if (prevChar == ' ' || prevChar == '\n') return;
                continue;
            }

            currX += charWidth;
            maxX = float.Max(maxX, currX);
        }
    }
    public Vector2 MeasureStringWrapped(ReadOnlySpan<char> text, float height, float posX, float wrapX, float? spacing = null, float? lineSpacing = null, int? index = null, int? length = null) {
        if (text.Length == 0) return Vector2.Zero;
        var ctx = new FontContext(this, height, spacing, lineSpacing);
        var (currX, currY, maxX) = (posX, height, posX);
        var origin = new Vector2(posX, 0);

        var slice = text.Slice(index ?? 0, length ?? text.Length);
        while (!slice.IsEmpty) {
            var nl = slice.IndexOf('\n');
            var line = nl < 0 ? slice : slice[..nl];

            measureWrappedSegment(line, ref currX, ref currY, ref maxX, origin, height, wrapX, in ctx);
            if (nl < 0) break;

            maxX = float.Max(maxX, currX);
            currX = posX;
            currY += height + ctx.LineSpacing;
            slice = slice[(nl + 1)..];
        }
        return new Vector2(maxX - posX, currY);
    }
    public Vector2 MeasureStringWrapped(List<List<char>> lines, float height, float posX, float wrapX, float? spacing = null, float? lineSpacing = null) {
        if (lines.Count == 0) return Vector2.Zero;
        var ctx = new FontContext(this, height, spacing, lineSpacing);
        var (currX, currY, maxX) = (posX, height, posX);
        var origin = new Vector2(posX, 0);

        for (var i = 0; i < lines.Count; i++) {
            measureWrappedSegment(CollectionsMarshal.AsSpan(lines[i]), ref currX, ref currY, ref maxX, origin, height, wrapX, in ctx);
            maxX = float.Max(maxX, currX);
            currX = posX;

            if (i < lines.Count - 1)
                currY += height + ctx.LineSpacing;
        }
        return new Vector2(maxX - posX, currY);
    }
    public void DrawStringWrapped(GuilBatch batch, string text, Vector2 position, Paint paint, float height, float wrapX, float? spacing = null, float? lineSpacing = null, int? index = null, int? length = null, float rotation = 0, List<Paint>? perCharColor = null, Alignment alignment = default) {
        DrawStringWrapped(batch, text.AsSpan(), position, paint, height, wrapX, spacing, lineSpacing, index, length, rotation, perCharColor, alignment);
    }
    public void DrawStringWrapped(GuilBatch batch, ReadOnlySpan<char> text, Vector2 position, Paint paint, float height, float wrapX, float? spacing = null, float? lineSpacing = null, int? index = null, int? length = null, float rotation = 0, List<Paint>? perCharColor = null, Alignment alignment = default) {
        wrapX = float.Max(position.X, wrapX);
        if (text.Length == 0 || wrapX == position.X) return;

        var ctx = new FontContext(this, height, spacing, lineSpacing, batch);
        var slice = text.Slice(index ?? 0, length ?? text.Length);

        var totalSizeX = wrapX - position.X;
        var totalSizeY = MeasureStringWrapped(slice, height, position.X, wrapX, spacing, lineSpacing).Y;
        var currY = position.Y;

        if (alignment.yAlignment == YAlignment.Center) currY -= totalSizeY / 2;
        else if (alignment.yAlignment == YAlignment.Bottom) currY -= totalSizeY;
        if (alignment.xAlignment == XAlignment.Center) {
            position.X -= totalSizeX / 2;
            wrapX -= totalSizeX / 2;
        }
        else if (alignment.xAlignment == XAlignment.Right) {
            position.X -= totalSizeX;
            wrapX -= totalSizeX;
        }

        var globalIndex = index ?? 0;

        while (!slice.IsEmpty) {
            var nl = slice.IndexOf('\n');
            var lineLength = nl < 0 ? slice.Length : nl;
            var line = slice[..lineLength];

            var vs = getVisualLineStarts(line, position.X, wrapX, in ctx);
            for (var v = 0; v < vs.Count; v++) {
                var start = vs[v];
                var end = v < vs.Count - 1 ? vs[v + 1] : line.Length;
                var subLine = line[start..end];

                var visibleLine = subLine.EndsWith(" ") && !subLine.EndsWith("  ") ? subLine.TrimEnd(" ") : subLine;
                var lineWidth = MeasureString(visibleLine, height, spacing).X;

                var currX = position.X;
                if (alignment.textAlignment == XAlignment.Center) currX += (totalSizeX - lineWidth) / 2;
                else if (alignment.textAlignment == XAlignment.Right) currX += totalSizeX - lineWidth;

                for (var i = 0; i < subLine.Length; i++) {
                    var c = subLine[i];
                    var charWidth = getCharWidth(c, in ctx);

                    if (c == ' ' || c == '\t') {
                        currX += charWidth;
                        continue;
                    }

                    if (ctx.Atlas.CharsData.TryGetValue(c, out var offset) || ctx.Atlas.CharsData.TryGetValue('?', out offset)) {
                        Rectangle sourceRect = new(offset.x, offset.y, offset.w, ctx.Atlas.Size);
                        var drawPos = new Vector2(currX, currY);
                        if (rotation != 0) drawPos.RotateAround(position, rotation);

                        var charPaint = paint;
                        if (perCharColor is not null) charPaint = perCharColor[globalIndex + start + i];

                        batch.DrawTexture(MegaAtlas, drawPos, sourceRect, charPaint, rotation, Vector2.Zero, ctx.AtlasScale, SpriteEffects.None, aaSize: 0);
                        currX += charWidth;
                    }
                }
                currY += height + ctx.LineSpacing;
            }

            if (nl < 0) break;
            globalIndex += nl + 1;
            slice = slice[(nl + 1)..];
        }
    }
    public void DrawStringWrapped(GuilBatch batch, List<List<char>> lines, Vector2 position, Paint paint, float height, float wrapX, float? spacing = null, float? lineSpacing = null, float rotation = 0, List<List<Paint>>? perCharColor = null, Alignment alignment = default) {
        wrapX = float.Max(position.X, wrapX);
        if (wrapX == position.X) return;

        var ctx = new FontContext(this, height, spacing, lineSpacing, batch);

        var totalSizeX = wrapX - position.X;
        var totalSizeY = MeasureStringWrapped(lines, height, position.X, wrapX, spacing, lineSpacing).Y;
        var currY = position.Y;

        if (alignment.yAlignment == YAlignment.Center) currY -= totalSizeY / 2;
        else if (alignment.yAlignment == YAlignment.Bottom) currY -= totalSizeY;
        if (alignment.xAlignment == XAlignment.Center) {
            position.X -= totalSizeX / 2;
            wrapX -= totalSizeX / 2;
        }
        else if (alignment.xAlignment == XAlignment.Right) {
            position.X -= totalSizeX;
            wrapX -= totalSizeX;
        }

        for (var j = 0; j < lines.Count; j++) {
            var span = CollectionsMarshal.AsSpan(lines[j]);

            var vs = getVisualLineStarts(span, position.X, wrapX, in ctx);
            for (var v = 0; v < vs.Count; v++) {
                var start = vs[v];
                var end = v < vs.Count - 1 ? vs[v + 1] : span.Length;
                var subLine = span[start..end];

                var visibleLine = subLine.EndsWith(" ") && !subLine.EndsWith("  ") ? subLine.TrimEnd(" ") : subLine;
                var lineWidth = MeasureString(visibleLine, height, spacing, null).X;

                var currX = position.X;
                if (alignment.textAlignment == XAlignment.Center) currX += (totalSizeX - lineWidth) / 2;
                else if (alignment.textAlignment == XAlignment.Right) currX += totalSizeX - lineWidth;

                for (var i = 0; i < subLine.Length; i++) {
                    var c = subLine[i];
                    var charWidth = getCharWidth(c, in ctx);

                    if (c == ' ' || c == '\t') {
                        currX += charWidth;
                        continue;
                    }

                    if (ctx.Atlas.CharsData.TryGetValue(c, out var offset) || ctx.Atlas.CharsData.TryGetValue('?', out offset)) {
                        Rectangle sourceRect = new(offset.x, offset.y, offset.w, ctx.Atlas.Size);
                        var drawPos = new Vector2(currX, currY);
                        if (rotation != 0) drawPos.RotateAround(position, rotation);

                        var charPaint = perCharColor is null ? paint : perCharColor[j][start + i];

                        batch.DrawTexture(MegaAtlas, drawPos, sourceRect, charPaint, rotation, Vector2.Zero, ctx.AtlasScale, SpriteEffects.None, aaSize: 0);
                        currX += charWidth;
                    }
                }
                if (j < lines.Count - 1 || v < vs.Count - 1) {
                    currY += height + ctx.LineSpacing;
                }
            }
        }
    }
    public void DrawStringOutlined(GuilBatch batch, string text, Vector2 position, Paint paint, Color outlineColor, float height, float outlineWidth, float step, float? spacing = null, float? lineSpacing = null, float rotation = 0, Alignment alignment = default) {
        for (float i = -1; i <= 1; i += step) {
            for (float j = -1; j <= 1; j += step) {
                var offset = new Vector2(i, j) * outlineWidth;
                if (rotation != 0) offset.Rotate(rotation);
                DrawString(batch, text, position + offset, outlineColor, height, spacing, lineSpacing, null, null, rotation, null, alignment);
            }
        }
        DrawString(batch, text, position, paint, height, spacing, lineSpacing, null, null, rotation, null, alignment);
    }
    public int GetIndexAt(ReadOnlySpan<char> line, float textX, float clickX, float height, float? spacing = null) {
        if (line.Length == 0) return 0;
        var ctx = new FontContext(this, height, spacing, null);
        var targetX = float.Max(textX, clickX);
        var cursorX = textX;

        for (var i = 0; i < line.Length; i++) {
            var charWidth = line[i] == '\n' ? 0 : getCharWidth(line[i], in ctx);
            var charRightX = cursorX + charWidth;

            if (charRightX > targetX)
                return targetX < (charRightX + cursorX) / 2 ? i : i + 1;

            cursorX = charRightX;
        }
        return line.Length;
    }
    public (int col, int ln) GetIndexAt(List<List<char>> lines, Vector2 position, Vector2 clickPos, float height, float? spacing = null, float? lineSpacing = null, Alignment alignment = default) {
        if (lines.Count == 0) return (0, 0);

        var ls = lineSpacing ?? LineSpacing;
        var totalSize = MeasureString(lines, height, spacing, lineSpacing);
        var startY = position.Y;

        if (alignment.yAlignment == YAlignment.Center) startY -= totalSize.Y / 2;
        else if (alignment.yAlignment == YAlignment.Bottom) startY -= totalSize.Y;
        if (alignment.xAlignment == XAlignment.Center) {
            position.X -= totalSize.X / 2;
        }
        else if (alignment.xAlignment == XAlignment.Right) {
            position.X -= totalSize.X;
        }

        var cy = float.Max(startY, clickPos.Y);
        var lineIndex = int.Clamp((int)float.Floor((cy - startY) / (height + ls)), 0, lines.Count - 1);
        var line = lines[lineIndex];
        var lineX = position.X;
        var lineWidth = MeasureString(line, height, spacing).X;
        if (alignment.textAlignment == XAlignment.Center) lineX += (totalSize.X - lineWidth) / 2;
        else if (alignment.textAlignment == XAlignment.Right) lineX += totalSize.X - lineWidth;

        return (GetIndexAt(CollectionsMarshal.AsSpan(line), lineX, clickPos.X, height, spacing), lineIndex);
    }
    public (int col, int ln) GetIndexAtWrapped(List<List<char>> lines, Vector2 position, Vector2 clickPos, float height, float wrapX, float? spacing = null, float? lineSpacing = null, Alignment alignment = default) {
        if (lines.Count == 0) return (0, 0);
        var ctx = new FontContext(this, height, spacing, lineSpacing);

        var totalSizeX = wrapX - position.X;
        var totalSizeY = MeasureStringWrapped(lines, height, position.X, wrapX, spacing, lineSpacing).Y;
        var startY = position.Y;

        if (alignment.yAlignment == YAlignment.Center) startY -= totalSizeY / 2;
        else if (alignment.yAlignment == YAlignment.Bottom) startY -= totalSizeY;
        if (alignment.xAlignment == XAlignment.Center) {
            position.X -= totalSizeX / 2;
            wrapX -= totalSizeX / 2;
        }
        else if (alignment.xAlignment == XAlignment.Right) {
            position.X -= totalSizeX;
            wrapX -= totalSizeX;
        }

        var targetVisualLine = int.Max(0, (int)float.Floor((clickPos.Y - startY) / (height + ctx.LineSpacing)));
        var visualLine = 0;

        for (var j = 0; j < lines.Count; j++) {
            var span = CollectionsMarshal.AsSpan(lines[j]);
            var visualStarts = getVisualLineStarts(span, position.X, wrapX, in ctx);

            for (var v = 0; v < visualStarts.Count; v++) {
                var isLast = j == lines.Count - 1 && v == visualStarts.Count - 1;
                if (visualLine == targetVisualLine || isLast) {
                    var segStart = visualStarts[v];
                    var segEnd = v < visualStarts.Count - 1 ? visualStarts[v + 1] : span.Length;
                    var subLine = span[segStart..segEnd];
                    var visibleLine = subLine.EndsWith(" ") && !subLine.EndsWith("  ") ? subLine.TrimEnd(" ") : subLine;
                    var lineWidth = MeasureString(visibleLine, height, spacing, null).X;
                    var lineStartX = position.X;
                    if (alignment.xAlignment == XAlignment.Center) lineStartX += (totalSizeX - lineWidth) / 2;
                    else if (alignment.xAlignment == XAlignment.Right) lineStartX += totalSizeX - lineWidth;

                    var col = segStart + GetIndexAt(subLine, lineStartX, clickPos.X, height, spacing);
                    return (col, j);
                }
                visualLine++;
            }
        }
        return (lines[^1].Count, lines.Count - 1);
    }
    public (int col, int ln) GetIndexAtWrapped(ReadOnlySpan<char> text, Vector2 position, Vector2 clickPos, float height, float wrapX, float? spacing = null, float? lineSpacing = null, Alignment alignment = default) {
        wrapX = float.Max(position.X, wrapX);
        if (text.Length == 0 || wrapX == position.X) return default;

        var ctx = new FontContext(this, height, spacing, lineSpacing);
        var totalSizeX = wrapX - position.X;
        var totalSizeY = MeasureStringWrapped(text, height, position.X, wrapX, spacing, lineSpacing).Y;
        var startY = position.Y;
        if (alignment.yAlignment == YAlignment.Center) startY -= totalSizeY / 2;
        else if (alignment.yAlignment == YAlignment.Bottom) startY -= totalSizeY;
        if (alignment.xAlignment == XAlignment.Center) {
            position.X -= totalSizeX / 2;
            wrapX -= totalSizeX / 2;
        }
        else if (alignment.xAlignment == XAlignment.Right) {
            position.X -= totalSizeX;
            wrapX -= totalSizeX;
        }

        var targetVisualLine = int.Max(0, (int)float.Floor((clickPos.Y - startY) / (height + ctx.LineSpacing)));
        var visualLine = 0;
        var remaining = text;
        var j = 0;

        int lastJ;
        int lastLineLen;
        while (true) {
            var newlineIdx = remaining.IndexOf('\n');
            var lineSpan = newlineIdx >= 0 ? remaining[..newlineIdx] : remaining;
            var isLastLogical = newlineIdx < 0;
            lastJ = j;
            lastLineLen = lineSpan.Length;

            var visualStarts = getVisualLineStarts(lineSpan, position.X, wrapX, in ctx);

            for (var v = 0; v < visualStarts.Count; v++) {
                var isLast = isLastLogical && v == visualStarts.Count - 1;
                if (visualLine == targetVisualLine || isLast) {
                    var segStart = visualStarts[v];
                    var segEnd = v < visualStarts.Count - 1 ? visualStarts[v + 1] : lineSpan.Length;
                    var subLine = lineSpan[segStart..segEnd];
                    var visibleLine = subLine.EndsWith(" ") && !subLine.EndsWith("  ") ? subLine.TrimEnd(' ') : subLine;
                    var lineWidth = MeasureString(visibleLine, height, spacing, null).X;
                    var lineStartX = position.X;
                    if (alignment.xAlignment == XAlignment.Center) lineStartX += (totalSizeX - lineWidth) / 2;
                    else if (alignment.xAlignment == XAlignment.Right) lineStartX += totalSizeX - lineWidth;

                    var col = segStart + GetIndexAt(subLine, lineStartX, clickPos.X, height, spacing);
                    return (col, j);
                }
                visualLine++;
            }

            if (isLastLogical) break;
            remaining = remaining[(newlineIdx + 1)..];
            j++;
        }

        return (lastLineLen, lastJ);
    }
    public Vector2 GetPositionAtWrapped(ReadOnlySpan<char> text, Vector2 position, (int col, int ln) index, float height, float wrapX, float? spacing = null, float? lineSpacing = null, Alignment alignment = default) {
        wrapX = float.Max(position.X, wrapX);
        if (text.Length == 0 || wrapX == position.X) return Vector2.Zero;

        var ctx = new FontContext(this, height, spacing, lineSpacing);
        var lineCount = 1;
        foreach (var c in text) if (c == '\n') lineCount++;
        index.ln = int.Clamp(index.ln, 0, lineCount - 1);

        var totalSizeX = wrapX - position.X;
        var totalSizeY = MeasureStringWrapped(text, height, position.X, wrapX, spacing, lineSpacing).Y;
        var currY = position.Y;
        if (alignment.yAlignment == YAlignment.Center) currY -= totalSizeY / 2;
        else if (alignment.yAlignment == YAlignment.Bottom) currY -= totalSizeY;
        if (alignment.xAlignment == XAlignment.Center) {
            position.X -= totalSizeX / 2;
            wrapX -= totalSizeX / 2;
        }
        else if (alignment.xAlignment == XAlignment.Right) {
            position.X -= totalSizeX;
            wrapX -= totalSizeX;
        }

        var remaining = text;
        var j = 0;
        ReadOnlySpan<char> targetLineSpan;
        while (true) {
            var newlineIdx = remaining.IndexOf('\n');
            var lineSpan = newlineIdx >= 0 ? remaining[..newlineIdx] : remaining;

            if (j == index.ln) {
                index.col = int.Clamp(index.col, 0, lineSpan.Length);
                targetLineSpan = lineSpan;
                break;
            }

            var vs = getVisualLineStarts(lineSpan, position.X, wrapX, in ctx);
            currY += vs.Count * (height + ctx.LineSpacing);

            if (newlineIdx < 0) { targetLineSpan = lineSpan; break; }
            remaining = remaining[(newlineIdx + 1)..];
            j++;
        }

        var visualStarts = getVisualLineStarts(targetLineSpan, position.X, wrapX, in ctx);

        var subLineIdx = 0;
        for (var v = 1; v < visualStarts.Count; v++) {
            if (visualStarts[v] <= index.col) subLineIdx = v;
            else break;
        }

        currY += subLineIdx * (height + ctx.LineSpacing);

        var segStart = visualStarts[subLineIdx];
        var segEnd = subLineIdx < visualStarts.Count - 1 ? visualStarts[subLineIdx + 1] : targetLineSpan.Length;
        var subLine = targetLineSpan[segStart..segEnd];

        var visibleLine = subLine.EndsWith(" ") && !subLine.EndsWith("  ") ? subLine.TrimEnd(' ') : subLine;
        var lineWidth = MeasureString(visibleLine, height, spacing, null).X;
        var currX = position.X;
        if (alignment.xAlignment == XAlignment.Center) currX += (totalSizeX - lineWidth) / 2;
        else if (alignment.xAlignment == XAlignment.Right) currX += totalSizeX - lineWidth;

        currX += MeasureString(targetLineSpan, height, spacing, null, segStart, index.col - segStart).X;
        return new Vector2(currX + ctx.Spacing / 2, currY);
    }
    public Vector2 GetPositionAtWrapped(List<List<char>> lines, Vector2 position, (int col, int ln) index, float height, float wrapX, float? spacing = null, float? lineSpacing = null, Alignment alignment = default) {
        wrapX = float.Max(position.X, wrapX);
        if (lines.Count == 0 || wrapX == position.X) return Vector2.Zero;

        var ctx = new FontContext(this, height, spacing, lineSpacing);
        index.ln = int.Clamp(index.ln, 0, lines.Count - 1);
        index.col = int.Clamp(index.col, 0, lines[index.ln].Count);

        var totalSizeX = wrapX - position.X;
        var totalSizeY = MeasureStringWrapped(lines, height, position.X, wrapX, spacing, lineSpacing).X;
        var currY = position.Y;
        if (alignment.yAlignment == YAlignment.Center) currY -= totalSizeY / 2;
        else if (alignment.yAlignment == YAlignment.Bottom) currY -= totalSizeY;
        if (alignment.xAlignment == XAlignment.Center) {
            position.X -= totalSizeX / 2;
            wrapX -= totalSizeX / 2;
        }
        else if (alignment.xAlignment == XAlignment.Right) {
            position.X -= totalSizeX;
            wrapX -= totalSizeX;
        }

        for (var j = 0; j < index.ln; j++) {
            var s = CollectionsMarshal.AsSpan(lines[j]);
            currY += getVisualLineStarts(s, position.X, wrapX, in ctx).Count * (height + ctx.LineSpacing);
        }

        var span = CollectionsMarshal.AsSpan(lines[index.ln]);
        var visualStarts = getVisualLineStarts(span, position.X, wrapX, in ctx);

        var subLineIdx = 0;
        for (var v = 1; v < visualStarts.Count; v++) {
            if (visualStarts[v] <= index.col) subLineIdx = v;
            else break;
        }

        currY += subLineIdx * (height + ctx.LineSpacing);

        var segStart = visualStarts[subLineIdx];
        var segEnd = subLineIdx < visualStarts.Count - 1 ? visualStarts[subLineIdx + 1] : span.Length;
        var subLine = span[segStart..segEnd];

        var visibleLine = subLine.EndsWith(" ") && !subLine.EndsWith("  ") ? subLine.TrimEnd(" ") : subLine;
        var lineWidth = MeasureString(visibleLine, height, spacing, null).X; var currX = position.X;
        if (alignment.xAlignment == XAlignment.Center) currX += (wrapX - position.X - lineWidth) / 2;
        else if (alignment.xAlignment == XAlignment.Right) currX += totalSizeX - lineWidth;

        currX += MeasureString(span, height, spacing, null, segStart, index.col - segStart).X;
        return new Vector2(currX + (index.col > 0 ? ctx.Spacing / 2 : -ctx.Spacing / 2), currY);
    }
    public void IterateSelectionRects(List<List<char>> lines, GuilBatch batch, Vector2 position, float height, float wrapX, (int col, int ln) start, (int col, int ln) end, Action<GuilBatch, RectangleF>? onRect = null, float padding = 0, float? spacing = null, float? lineSpacing = null, Alignment alignment = default) {
        wrapX = float.Max(position.X, wrapX);
        if (lines.Count == 0 || wrapX == position.X) return;
        var ctx = new FontContext(this, height, spacing, lineSpacing, batch);
        var visualLine = 0;

        var totalSizeX = wrapX - position.X;
        var totalSizeY = MeasureStringWrapped(lines, height, position.X, wrapX, spacing, lineSpacing).X;
        if (alignment.yAlignment == YAlignment.Center) position.Y -= totalSizeY / 2;
        else if (alignment.yAlignment == YAlignment.Bottom) position.Y -= totalSizeY;
        if (alignment.xAlignment == XAlignment.Center) {
            position.X -= totalSizeX / 2;
            wrapX -= totalSizeX / 2;
        }
        else if (alignment.xAlignment == XAlignment.Right) {
            position.X -= totalSizeX;
            wrapX -= totalSizeX;
        }

        for (var j = 0; j < lines.Count; j++) {
            var span = CollectionsMarshal.AsSpan(lines[j]);
            var vs = getVisualLineStarts(span, position.X, wrapX, in ctx);

            for (var v = 0; v < vs.Count; v++) {
                var subStart = vs[v];
                var subEnd = v < vs.Count - 1 ? vs[v + 1] : span.Length;
                var subLine = span[subStart..subEnd];

                var inStart = j > start.ln || (j == start.ln && subEnd > start.col);
                var inEnd = j < end.ln || (j == end.ln && subStart < end.col);

                if (inStart && inEnd) {
                    var visibleLine = subLine.EndsWith(" ") && !subLine.EndsWith("  ") ? subLine.TrimEnd(" ") : subLine;
                    var lineWidth = MeasureString(subLine, height, spacing, null).X;

                    var lineStartX = position.X;
                    if (alignment.xAlignment == XAlignment.Center) lineStartX += (totalSizeX - lineWidth) / 2;
                    else if (alignment.xAlignment == XAlignment.Right) lineStartX -= lineWidth - totalSizeX;

                    var x1 = lineStartX + ((j == start.ln && start.col > subStart)
                        ? MeasureString(span, height, spacing, null, subStart, start.col - subStart).X + ctx.Spacing / 2
                        : -ctx.Spacing / 2);

                    var x2 = lineStartX + ((j == end.ln && end.col < subEnd)
                        ? MeasureString(span, height, spacing, null, subStart, end.col - subStart).X
                        : lineWidth) + ctx.Spacing / 2;
                    var lineRect = new RectangleF(
                        x1 - padding,
                        position.Y - padding + (height + ctx.LineSpacing) * visualLine,
                        x2 - x1 + padding * 2,
                        height + padding * 2
                    );
                    onRect?.Invoke(batch, lineRect);
                }
                visualLine++;
            }
        }
    }
    public void Dispose() {
        MegaAtlas?.Dispose();
        _atlases.Clear();
    }
}