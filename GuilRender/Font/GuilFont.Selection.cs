using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Guilred.Shapes;
using Microsoft.Xna.Framework;
using static Guilred.Rendering.GuilFontUtils;

namespace Guilred.Rendering;

public sealed partial class GuilFont {
	public int GetIndexAt(ReadOnlySpan<char> line, float textX, float clickX, float height, float? spacing = null) {
		if (line.Length == 0) return 0;
		var ctx = new FontContext(this, height, spacing, null);
		var targetX = float.Max(textX, clickX);
		var cursorX = textX;

		for (var i = 0; i < line.Length; i++) {
			var charWidth = line[i] == '\n' ? 0 : GetCharWidth(line[i], in ctx);
			var charRightX = cursorX + charWidth;

			if (charRightX > targetX)
				return targetX < (charRightX + cursorX) / 2 ? i : i + 1;

			cursorX = charRightX;
		}
		return line.Length;
	}

	public (int col, int ln) GetIndexAt(List<List<char>> lines, Vector2 position, Vector2 clickPos, float height, float? spacing = null, float? lineSpacing = null, Rotation rotation = default, Alignment alignment = default) {
		if (lines.Count == 0) return (0, 0);

		var ls = lineSpacing ?? LineSpacing;
		var totalSize = MeasureString(lines, height, spacing, lineSpacing);
		var startY = position.Y;
		var pivot = ApplyAlignment(ref position.X, ref startY, totalSize, alignment, rotation);

		if (rotation.Angle != 0) clickPos.RotateAround(pivot, -rotation.Angle);

		var cy = float.Max(startY, clickPos.Y);
		var lineIndex = int.Clamp((int)float.Floor((cy - startY) / (height + ls)), 0, lines.Count - 1);
		var line = lines[lineIndex];
		var lineWidth = MeasureString(line, height, spacing).X;
		var lineX = GetAlignedLineX(position.X, totalSize.X, lineWidth, alignment.textAlignment);

		return (GetIndexAt(CollectionsMarshal.AsSpan(line), lineX, clickPos.X, height, spacing), lineIndex);
	}

	public (int col, int ln) GetIndexAtWrapped(List<List<char>> lines, Vector2 position, Vector2 clickPos, float height, float wrapX, float? spacing = null, float? lineSpacing = null, Rotation rotation = default, Alignment alignment = default) {
		if (lines.Count == 0) return (0, 0);
		var ctx = new FontContext(this, height, spacing, lineSpacing);

		var totalSize = new Vector2(wrapX - position.X, MeasureStringWrapped(lines, height, position.X, wrapX, spacing, lineSpacing).Y);
		var startY = position.Y;
		var pivot = ApplyAlignment(ref position.X, ref startY, ref wrapX, totalSize, alignment, rotation);

		if (rotation.Angle != 0) clickPos.RotateAround(pivot, -rotation.Angle);

		var targetVisualLine = int.Max(0, (int)float.Floor((clickPos.Y - startY) / (height + ctx.LineSpacing)));
		var visualLine = 0;

		for (var j = 0; j < lines.Count; j++) {
			var span = CollectionsMarshal.AsSpan(lines[j]);
			var visualStarts = GetVisualLineStarts(span, position.X, wrapX, in ctx);

			for (var v = 0; v < visualStarts.Count; v++) {
				var isLast = j == lines.Count - 1 && v == visualStarts.Count - 1;
				if (visualLine == targetVisualLine || isLast) {
					var subLine = GetSubLine(span, visualStarts, v, out var segStart, out _);
					var visibleLine = TrimSingleTrailingSpace(subLine);
					var lineWidth = MeasureString(visibleLine, height, spacing, null).X;
					var lineStartX = GetAlignedLineX(position.X, totalSize.X, lineWidth, alignment.textAlignment);

					var col = segStart + GetIndexAt(subLine, lineStartX, clickPos.X, height, spacing);
					return (col, j);
				}
				visualLine++;
			}
		}
		return (lines[^1].Count, lines.Count - 1);
	}

	public (int col, int ln) GetIndexAtWrapped(ReadOnlySpan<char> text, Vector2 position, Vector2 clickPos, float height, float wrapX, float? spacing = null, float? lineSpacing = null, Rotation rotation = default, Alignment alignment = default) {
		wrapX = float.Max(position.X, wrapX);
		if (text.Length == 0 || wrapX == position.X) return default;

		var ctx = new FontContext(this, height, spacing, lineSpacing);
		var totalSize = new Vector2(wrapX - position.X, MeasureStringWrapped(text, height, position.X, wrapX, spacing, lineSpacing).Y);
		var startY = position.Y;
		var pivot = ApplyAlignment(ref position.X, ref startY, ref wrapX, totalSize, alignment, rotation);

		if (rotation.Angle != 0) clickPos.RotateAround(pivot, -rotation.Angle);

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

			var visualStarts = GetVisualLineStarts(lineSpan, position.X, wrapX, in ctx);

			for (var v = 0; v < visualStarts.Count; v++) {
				var isLast = isLastLogical && v == visualStarts.Count - 1;
				if (visualLine == targetVisualLine || isLast) {
					var subLine = GetSubLine(lineSpan, visualStarts, v, out var segStart, out _);
					var visibleLine = TrimSingleTrailingSpace(subLine);
					var lineWidth = MeasureString(visibleLine, height, spacing, null).X;
					var lineStartX = GetAlignedLineX(position.X, totalSize.X, lineWidth, alignment.textAlignment);

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

	public Vector2 GetPositionAtWrapped(ReadOnlySpan<char> text, Vector2 position, (int col, int ln) index, float height, float wrapX, float? spacing = null, float? lineSpacing = null, Rotation rotation = default, Alignment alignment = default) {
		wrapX = float.Max(position.X, wrapX);
		if (text.Length == 0 || wrapX == position.X) return Vector2.Zero;

		var ctx = new FontContext(this, height, spacing, lineSpacing);
		var lineCount = 1;
		foreach (var c in text) if (c == '\n') lineCount++;
		index.ln = int.Clamp(index.ln, 0, lineCount - 1);

		var totalSize = new Vector2(wrapX - position.X, MeasureStringWrapped(text, height, position.X, wrapX, spacing, lineSpacing).Y);
		var currY = position.Y;
		var pivot = ApplyAlignment(ref position.X, ref currY, ref wrapX, totalSize, alignment, rotation);

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

			var vs = GetVisualLineStarts(lineSpan, position.X, wrapX, in ctx);
			currY += vs.Count * (height + ctx.LineSpacing);

			if (newlineIdx < 0) { targetLineSpan = lineSpan; break; }
			remaining = remaining[(newlineIdx + 1)..];
			j++;
		}

		var visualStarts = GetVisualLineStarts(targetLineSpan, position.X, wrapX, in ctx);
		var subLineIdx = FindVisualSubLineIndex(visualStarts, index.col);
		currY += subLineIdx * (height + ctx.LineSpacing);

		var subLine = GetSubLine(targetLineSpan, visualStarts, subLineIdx, out var segStart, out _);
		var visibleLine = TrimSingleTrailingSpace(subLine);
		var lineWidth = MeasureString(visibleLine, height, spacing, null).X;
		var currX = GetAlignedLineX(position.X, totalSize.X, lineWidth, alignment.textAlignment);

		currX += MeasureString(targetLineSpan, height, spacing, null, segStart, index.col - segStart).X;

		var resultPos = new Vector2(currX + ctx.Spacing / 2, currY);
		if (rotation.Angle != 0) resultPos.RotateAround(pivot, rotation.Angle);
		return resultPos;
	}

	public Vector2 GetPositionAtWrapped(List<List<char>> lines, Vector2 position, (int col, int ln) index, float height, float wrapX, float? spacing = null, float? lineSpacing = null, Rotation rotation = default, Alignment alignment = default) {
		wrapX = float.Max(position.X, wrapX);
		if (lines.Count == 0 || wrapX == position.X) return Vector2.Zero;

		var ctx = new FontContext(this, height, spacing, lineSpacing);
		index.ln = int.Clamp(index.ln, 0, lines.Count - 1);
		index.col = int.Clamp(index.col, 0, lines[index.ln].Count);

		var totalSize = new Vector2(wrapX - position.X, MeasureStringWrapped(lines, height, position.X, wrapX, spacing, lineSpacing).Y);
		var currY = position.Y;
		var pivot = ApplyAlignment(ref position.X, ref currY, ref wrapX, totalSize, alignment, rotation);

		for (var j = 0; j < index.ln; j++) {
			var s = CollectionsMarshal.AsSpan(lines[j]);
			currY += GetVisualLineStarts(s, position.X, wrapX, in ctx).Count * (height + ctx.LineSpacing);
		}

		var span = CollectionsMarshal.AsSpan(lines[index.ln]);
		var visualStarts = GetVisualLineStarts(span, position.X, wrapX, in ctx);
		var subLineIdx = FindVisualSubLineIndex(visualStarts, index.col);
		currY += subLineIdx * (height + ctx.LineSpacing);

		var subLine = GetSubLine(span, visualStarts, subLineIdx, out var segStart, out _);
		var visibleLine = TrimSingleTrailingSpace(subLine);
		var lineWidth = MeasureString(visibleLine, height, spacing, null).X;
		var currX = GetAlignedLineX(position.X, totalSize.X, lineWidth, alignment.textAlignment);

		currX += MeasureString(span, height, spacing, null, segStart, index.col - segStart).X;

		var resultPos = new Vector2(currX + (index.col > 0 ? ctx.Spacing / 2 : -ctx.Spacing / 2), currY);
		if (rotation.Angle != 0) resultPos.RotateAround(pivot, rotation.Angle);
		return resultPos;
	}

	public interface IRectCallback {
		void Invoke(GuilBatch batch, RectangleF rect);
	}
	public void IterateSelectionRects<TCallback>(
			List<List<char>> lines,
			GuilBatch batch,
			Vector2 position,
			float height,
			float wrapX,
			(int col, int ln) start,
			(int col, int ln) end,
			TCallback callback,
			float padding = 0,
			float? spacing = null,
			float? lineSpacing = null,
			Alignment alignment = default
		) where TCallback : struct, IRectCallback {

		wrapX = float.Max(position.X, wrapX);
		if (lines.Count == 0 || wrapX == position.X) return;
		var ctx = new FontContext(this, height, spacing, lineSpacing, batch);
		var visualLine = 0;

		var totalSize = new Vector2(wrapX - position.X, MeasureStringWrapped(lines, height, position.X, wrapX, spacing, lineSpacing).Y);
		ApplyAlignment(ref position.X, ref position.Y, ref wrapX, totalSize, alignment, default);

		for (var j = 0; j < lines.Count; j++) {
			var span = CollectionsMarshal.AsSpan(lines[j]);
			var vs = GetVisualLineStarts(span, position.X, wrapX, in ctx);

			for (var v = 0; v < vs.Count; v++) {
				var subLine = GetSubLine(span, vs, v, out var subStart, out var subEnd);

				var inStart = j > start.ln || (j == start.ln && subEnd > start.col);
				var inEnd = j < end.ln || (j == end.ln && subStart < end.col);

				if (inStart && inEnd) {
					var visibleLine = TrimSingleTrailingSpace(subLine);
					var lineWidth = MeasureString(visibleLine, height, spacing, null).X;
					var lineStartX = GetAlignedLineX(position.X, totalSize.X, lineWidth, alignment.textAlignment);

					var x1 = lineStartX + ((j == start.ln && start.col > subStart)
						? MeasureString(span, height, spacing, null, subStart, start.col - subStart).X + ctx.Spacing / 2
						: -ctx.Spacing / 2);

					var x2 = lineStartX + ((j == end.ln && end.col < subEnd)
						? MeasureString(span, height, spacing, null, subStart, end.col - subStart).X
						: lineWidth) + ctx.Spacing / 2;

					var lineRect = new RectangleF(
						x1, position.Y + (height + ctx.LineSpacing) * visualLine,
						x2 - x1, height
					);
					lineRect.Inflate(padding);
					callback.Invoke(batch, lineRect);
				}
				visualLine++;
			}
		}
	}
}