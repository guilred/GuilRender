using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Guilred.Rendering;

internal static class GuilFontUtils {
	public static Vector2 ApplyAlignment(
		ref float posX,
		ref float posY,
		ref float wrapX,
		Vector2 totalSize,
		Alignment alignment,
		Rotation rotation
	) {
		if (alignment.yAlignment == YAlignment.Center) posY -= totalSize.Y / 2f;
		else if (alignment.yAlignment == YAlignment.Bottom) posY -= totalSize.Y;

		if (alignment.xAlignment == XAlignment.Center) {
			posX -= totalSize.X / 2f;
			wrapX -= totalSize.X / 2f;
		}
		else if (alignment.xAlignment == XAlignment.Right) {
			posX -= totalSize.X;
			wrapX -= totalSize.X;
		}

		return rotation.Pivot ?? new Vector2(posX + totalSize.X / 2f, posY + totalSize.Y / 2f);
	}

	public static Vector2 ApplyAlignment(
		ref float posX,
		ref float posY,
		Vector2 totalSize,
		Alignment alignment,
		Rotation rotation
	) {
		var dummyWrapX = 0f;
		return ApplyAlignment(ref posX, ref posY, ref dummyWrapX, totalSize, alignment, rotation);
	}

	public static float GetAlignedLineX(float boxX, float totalWidth, float lineWidth, XAlignment textAlignment) {
		return textAlignment switch {
			XAlignment.Center => boxX + (totalWidth - lineWidth) / 2f,
			XAlignment.Right => boxX + totalWidth - lineWidth,
			_ => boxX
		};
	}

	public static ReadOnlySpan<char> GetSubLine(
		ReadOnlySpan<char> line,
		List<int> visualStarts,
		int visualIndex,
		out int segStart,
		out int segEnd
	) {
		segStart = visualStarts[visualIndex];
		segEnd = visualIndex < visualStarts.Count - 1 ? visualStarts[visualIndex + 1] : line.Length;
		return line[segStart..segEnd];
	}

	public static ReadOnlySpan<char> TrimSingleTrailingSpace(ReadOnlySpan<char> subLine) {
		return subLine.EndsWith(" ") && !subLine.EndsWith("  ")
			? subLine.TrimEnd(' ')
			: subLine;
	}

	public static int FindVisualSubLineIndex(List<int> visualStarts, int col) {
		var subLineIdx = 0;
		for (var v = 1; v < visualStarts.Count; v++) {
			if (visualStarts[v] <= col) subLineIdx = v;
			else break;
		}
		return subLineIdx;
	}

	public static float MeasureWordWidth(ReadOnlySpan<char> segment, int startIndex, in GuilFont.FontContext ctx) {
		var wordWidth = 0f;
		for (var k = startIndex; k < segment.Length; k++) {
			var wc = segment[k];
			if (wc == ' ' || wc == '\n') break;
			wordWidth += GuilFont.GetCharWidth(wc, in ctx);
		}
		return wordWidth;
	}

	public static void DrawGlyphLine(
		GuilFont font,
		GuilBatch batch,
		ReadOnlySpan<char> line,
		float startX,
		float currY,
		Paint defaultPaint,
		in GuilFont.FontContext ctx,
		Rotation rotation,
		Vector2 pivot,
		List<Paint>? perCharColor = null,
		int paintOffset = 0,
		bool useDrawPosAsRotationOrigin = false
	) {
		var currX = startX;
		var atlases = font.Atlases;

		for (var i = 0; i < line.Length; i++) {
			var c = line[i];
			var charWidth = GuilFont.GetCharWidth(c, in ctx);

			if (c == ' ' || c == '\t') {
				currX += charWidth;
				continue;
			}

			if (ctx.Atlas.CharsData.TryGetValue(c, out var offset) || ctx.Atlas.CharsData.TryGetValue('?', out offset)) {
				Rectangle sourceRect = new(offset.x, offset.y, offset.w, ctx.Atlas.Size);
				var drawPos = new Vector2(currX, currY);
				if (rotation.Angle != 0) drawPos.RotateAround(pivot, rotation.Angle);

				var charPaint = perCharColor is not null ? perCharColor[paintOffset + i] : defaultPaint;
				var origin = useDrawPosAsRotationOrigin ? drawPos : Vector2.Zero;

				batch.DrawTexture(
					atlases[offset.texIndex],
					drawPos,
					sourceRect,
					charPaint,
					(rotation.Angle, origin),
					ctx.AtlasScale,
					SpriteEffects.None,
					aaSize: 0
				);
				currX += charWidth;
			}
		}
	}
}