using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using static Guilred.Rendering.GuilFontUtils;

namespace Guilred.Rendering;

public sealed partial class GuilFont {
	public void DrawString(GuilBatch batch, string text, Vector2 position, Paint paint, float height, float? spacing = null, float? lineSpacing = null, int? index = null, int? length = null, Rotation rotation = default, List<Paint>? perCharColor = null, Alignment alignment = default) {
		DrawString(batch, text.AsSpan(), position, paint, height, spacing, lineSpacing, index, length, rotation, perCharColor, alignment);
	}

	public void DrawString(GuilBatch batch, ReadOnlySpan<char> text, Vector2 position, Paint paint, float height, float? spacing = null, float? lineSpacing = null, int? index = null, int? length = null, Rotation rotation = default, List<Paint>? perCharColor = null, Alignment alignment = default) {
		if (text.Length == 0) return;
		var ctx = new FontContext(this, height, spacing, lineSpacing, batch);
		var startIdx = index ?? 0;
		var slice = text.Slice(startIdx, length ?? (text.Length - startIdx));

		var totalSize = MeasureString(slice, height, spacing, lineSpacing);
		var currY = position.Y;
		var pivot = ApplyAlignment(ref position.X, ref currY, totalSize, alignment, rotation);
		var globalIndex = startIdx;

		while (!slice.IsEmpty) {
			var nl = slice.IndexOf('\n');
			var lineLength = nl < 0 ? slice.Length : nl;
			var line = slice[..lineLength];

			var lineWidth = MeasureString(line, height, spacing, lineSpacing).X;
			var currX = GetAlignedLineX(position.X, totalSize.X, lineWidth, alignment.textAlignment);

			DrawGlyphLine(
				this, batch, line, currX, currY, paint, in ctx, rotation, pivot,
				perCharColor, globalIndex, useDrawPosAsRotationOrigin: true
			);

			if (nl < 0) break;
			currY += height + ctx.LineSpacing;
			globalIndex += nl + 1;
			slice = slice[(nl + 1)..];
		}
	}

	public void DrawStringOutlined(GuilBatch batch, string text, Vector2 position, Paint paint, Color outlineColor, float height, float outlineWidth, float step, float? spacing = null, float? lineSpacing = null, Rotation rotation = default, Alignment alignment = default) {
		var explicitRotation = rotation;

		if (rotation.Pivot == null) {
			var totalSize = MeasureString(text, height, spacing, lineSpacing);
			var topX = position.X;
			var topY = position.Y;
			var pivot = ApplyAlignment(ref topX, ref topY, totalSize, alignment, rotation);
			explicitRotation = new Rotation(rotation.Angle, pivot);
		}

		for (float i = -1; i <= 1; i += step) {
			for (float j = -1; j <= 1; j += step) {
				var offset = new Vector2(i, j) * outlineWidth;
				if (explicitRotation.Angle != 0) offset.Rotate(explicitRotation.Angle);
				DrawString(batch, text, position + offset, outlineColor, height, spacing, lineSpacing, null, null, explicitRotation, null, alignment);
			}
		}
		DrawString(batch, text, position, paint, height, spacing, lineSpacing, null, null, explicitRotation, null, alignment);
	}
}