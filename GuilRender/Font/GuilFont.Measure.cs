using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace Guilred.Rendering;

public sealed partial class GuilFont {
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

		var startIdx = index ?? 0;
		var slice = text.Slice(startIdx, length ?? (text.Length - startIdx));

		for (var i = 0; i < slice.Length; i++) {
			var c = slice[i];
			if (c == '\n') {
				size.Y += height + ctx.LineSpacing;
				size.X = float.Max(size.X, currWidth - ctx.Spacing);
				currWidth = 0;
				continue;
			}
			currWidth += GuilFontUtils.GetCharWidth(c, in ctx);
		}
		size.X = float.Max(size.X, currWidth - ctx.Spacing);
		return size;
	}

	public Vector2 MeasureString(List<List<char>> lines, float height, float? spacing = null, float? lineSpacing = null) {
		if (lines.Count == 0) return Vector2.Zero;
		var currLS = lineSpacing ?? LineSpacing;
		var size = Vector2.UnitY * (height * lines.Count + currLS * (lines.Count - 1));
		for (int i = 0; i < lines.Count; i++) {
			size.X = float.Max(size.X, MeasureString(lines[i], height, spacing, currLS).X);
		}
		return size;
	}
}