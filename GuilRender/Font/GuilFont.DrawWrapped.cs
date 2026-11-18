using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using static Guilred.Rendering.GuilFontUtils;

namespace Guilred.Rendering;

public sealed partial class GuilFont {
	public void DrawStringWrapped(GuilBatch batch, string text, Vector2 position, Paint paint, float height, float wrapX, float? spacing = null, float? lineSpacing = null, int? index = null, int? length = null, Rotation rotation = default, List<Paint>? perCharColor = null, Alignment alignment = default) {
		DrawStringWrapped(batch, text.AsSpan(), position, paint, height, wrapX, spacing, lineSpacing, index, length, rotation, perCharColor, alignment);
	}

	public void DrawStringWrapped(GuilBatch batch, ReadOnlySpan<char> text, Vector2 position, Paint paint, float height, float wrapX, float? spacing = null, float? lineSpacing = null, int? index = null, int? length = null, Rotation rotation = default, List<Paint>? perCharColor = null, Alignment alignment = default) {
		wrapX = float.Max(position.X, wrapX);
		if (text.Length == 0 || wrapX == position.X) return;

		var ctx = new FontContext(this, height, spacing, lineSpacing, batch);
		var startIdx = index ?? 0;
		var slice = text.Slice(startIdx, length ?? (text.Length - startIdx));

		var totalSize = new Vector2(wrapX - position.X, MeasureStringWrapped(slice, height, position.X, wrapX, spacing, lineSpacing).Y);
		var currY = position.Y;
		var pivot = ApplyAlignment(ref position.X, ref currY, ref wrapX, totalSize, alignment, rotation);
		var globalIndex = startIdx;

		while (!slice.IsEmpty) {
			var nl = slice.IndexOf('\n');
			var lineLength = nl < 0 ? slice.Length : nl;
			var line = slice[..lineLength];

			var vs = GetVisualLineStarts(line, position.X, wrapX, in ctx);
			for (var v = 0; v < vs.Count; v++) {
				var subLine = GetSubLine(line, vs, v, out var segStart, out _);
				var visibleLine = TrimSingleTrailingSpace(subLine);
				var lineWidth = MeasureString(visibleLine, height, spacing).X;
				var currX = GetAlignedLineX(position.X, totalSize.X, lineWidth, alignment.textAlignment);

				DrawGlyphLine(
					this, batch, subLine, currX, currY, paint, in ctx, rotation, pivot,
					perCharColor, globalIndex + segStart
				);

				currY += height + ctx.LineSpacing;
			}

			if (nl < 0) break;
			globalIndex += nl + 1;
			slice = slice[(nl + 1)..];
		}
	}

	public void DrawStringWrapped(GuilBatch batch, List<List<char>> lines, Vector2 position, Paint paint, float height, float wrapX, float? spacing = null, float? lineSpacing = null, Rotation rotation = default, List<List<Paint>>? perCharColor = null, Alignment alignment = default) {
		wrapX = float.Max(position.X, wrapX);
		if (wrapX == position.X) return;

		var ctx = new FontContext(this, height, spacing, lineSpacing, batch);

		var totalSize = new Vector2(wrapX - position.X, MeasureStringWrapped(lines, height, position.X, wrapX, spacing, lineSpacing).Y);
		var currY = position.Y;
		var pivot = ApplyAlignment(ref position.X, ref currY, ref wrapX, totalSize, alignment, rotation);

		for (var j = 0; j < lines.Count; j++) {
			var span = CollectionsMarshal.AsSpan(lines[j]);
			var lineColors = perCharColor?[j];

			var vs = GetVisualLineStarts(span, position.X, wrapX, in ctx);
			for (var v = 0; v < vs.Count; v++) {
				var subLine = GetSubLine(span, vs, v, out var segStart, out _);
				var visibleLine = TrimSingleTrailingSpace(subLine);
				var lineWidth = MeasureString(visibleLine, height, spacing, null).X;
				var currX = GetAlignedLineX(position.X, totalSize.X, lineWidth, alignment.textAlignment);

				DrawGlyphLine(
					this, batch, subLine, currX, currY, paint, in ctx, rotation, pivot,
					lineColors, segStart
				);

				if (j < lines.Count - 1 || v < vs.Count - 1) {
					currY += height + ctx.LineSpacing;
				}
			}
		}
	}
}