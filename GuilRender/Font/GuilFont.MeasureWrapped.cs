using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using static Guilred.Rendering.GuilFontUtils;

namespace Guilred.Rendering;

public sealed partial class GuilFont {
	private List<int> GetVisualLineStarts(ReadOnlySpan<char> segment, float posX, float wrapX, in FontContext ctx) {
		_starts.Clear();
		_starts.Add(0);
		var currX = posX;

		for (var i = 0; i < segment.Length; i++) {
			var c = segment[i];
			if (c == '\n') continue;

			var charWidth = GetCharWidth(c, in ctx);
			if (c == ' ' || c == '\t') { currX += charWidth; continue; }

			var prevChar = i > 0 ? segment[i - 1] : ' ';
			if (prevChar == ' ' || prevChar == '\n') {
				var wordWidth = MeasureWordWidth(segment, i, in ctx);
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

	private static void MeasureWrappedSegment(ReadOnlySpan<char> segment, ref float currX, ref float currY, ref float maxX, Vector2 position, float height, float wrapX, in FontContext ctx) {
		for (var i = 0; i < segment.Length; i++) {
			var c = segment[i];
			if (c == '\n') continue;

			var charWidth = GetCharWidth(c, in ctx);
			if (c == ' ' || c == '\t') {
				currX += charWidth;
				continue;
			}

			var prevChar = i > 0 ? segment[i - 1] : ' ';
			if (prevChar == ' ' || prevChar == '\n') {
				var wordWidth = MeasureWordWidth(segment, i, in ctx);
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

			currX += charWidth;
			maxX = float.Max(maxX, currX);
		}
	}

	public Vector2 MeasureStringWrapped(ReadOnlySpan<char> text, float height, float posX, float wrapX, float? spacing = null, float? lineSpacing = null, int? index = null, int? length = null) {
		if (text.Length == 0) return Vector2.Zero;
		var ctx = new FontContext(this, height, spacing, lineSpacing);
		var (currX, currY, maxX) = (posX, height, posX);
		var origin = new Vector2(posX, 0);

		var startIdx = index ?? 0;
		var slice = text.Slice(startIdx, length ?? (text.Length - startIdx));
		while (!slice.IsEmpty) {
			var nl = slice.IndexOf('\n');
			var line = nl < 0 ? slice : slice[..nl];

			MeasureWrappedSegment(line, ref currX, ref currY, ref maxX, origin, height, wrapX, in ctx);
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
			MeasureWrappedSegment(CollectionsMarshal.AsSpan(lines[i]), ref currX, ref currY, ref maxX, origin, height, wrapX, in ctx);
			maxX = float.Max(maxX, currX);
			currX = posX;

			if (i < lines.Count - 1)
				currY += height + ctx.LineSpacing;
		}
		return new Vector2(maxX - posX, currY);
	}
}