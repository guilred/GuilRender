using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Guilred.Rendering;

public sealed partial class GuilFont : IDisposable {
	internal class AtlasData {
		public Dictionary<char, (int texIndex, int x, int y, int w)> CharsData = [];
		public int Size;
	}

	private readonly List<AtlasData> _atlases = [];
	private readonly List<Texture2D> _textureAtlases = [];
	private readonly List<int> _starts = new(256);

	public IReadOnlyList<Texture2D> Atlases => _textureAtlases;
	public float Spacing { get; set; } = 5;
	public float LineSpacing { get; set; } = 5;

	public GuilFont(GraphicsDevice graphics, GuilBatch batch, string guifFilePath, GraphicsProfile profile = GraphicsProfile.Reach) {
		using var fileStream = TitleContainer.OpenStream(guifFilePath);
		using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

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

		var maxAtlasSize = profile == GraphicsProfile.Reach ? 2048 : 4096;
		var currentAtlasIndex = 0;

		List<(int stripH, char c, int x, int y, int w, Texture2D stripTex, int sx, int atlasIndex)> packedGlyphs = [];

		foreach (var g in glyphsToPack) {
			if (currentX + g.w + padding > maxAtlasSize) {
				currentX = padding;
				currentY += currentRowHeight + padding;
				currentRowHeight = 0;
			}

			if (currentY + g.stripH + padding > maxAtlasSize) {
				currentAtlasIndex++;
				currentX = padding;
				currentY = padding;
				currentRowHeight = 0;
			}

			packedGlyphs.Add((g.stripH, g.c, currentX, currentY, g.w, g.stripTex, g.sx, currentAtlasIndex));
			currentX += g.w + padding;
			currentRowHeight = int.Max(currentRowHeight, g.stripH);
		}

		var totalAtlases = currentAtlasIndex + 1;

		for (int i = 0; i < totalAtlases; i++) {
			int height = (i == totalAtlases - 1) ? currentY + currentRowHeight + padding : maxAtlasSize;
			var renderTarget = new RenderTarget2D(graphics, maxAtlasSize, height);

			graphics.SetRenderTarget(renderTarget);
			graphics.Clear(Color.Transparent);
			batch.Begin(blendState: BlendState.NonPremultiplied);

			var glyphsOnThisAtlas = packedGlyphs.Where(g => g.atlasIndex == i);
			foreach (var g in glyphsOnThisAtlas) {
				batch.DrawTexture(g.stripTex, new Vector2(g.x, g.y), new Rectangle(g.sx, 0, g.w, g.stripH), Color.White, aaSize: 0);
			}

			batch.End();
			_textureAtlases.Add(renderTarget);
		}
		graphics.SetRenderTarget(null);

		foreach (var tex in stripTextures) tex.Dispose();

		var grouped = packedGlyphs.GroupBy(g => g.stripH).OrderBy(g => g.Key);
		foreach (var group in grouped) {
			var atlasData = new AtlasData { Size = group.Key };
			foreach (var g in group) {
				atlasData.CharsData[g.c] = (g.atlasIndex, g.x, g.y, g.w);
			}
			_atlases.Add(atlasData);
		}
	}

	public float GetSpacing(float height) {
		return Spacing * height / 120f;
	}

	private AtlasData GetBestAtlas(float targetHeight) {
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

	internal readonly struct FontContext {
		public readonly AtlasData Atlas;
		public readonly AtlasData LargestAtlas;
		public readonly float AtlasScale;
		public readonly float LAtlasScale;
		public readonly float Spacing;
		public readonly float LineSpacing;
		public readonly float SpacingScale;

		public FontContext(GuilFont font, float height, float? spacing, float? lineSpacing, GuilBatch? batch = null) {
			var zoom = batch?.CameraZoom ?? 1;
			(Atlas, LargestAtlas) = (font.GetBestAtlas(height * zoom), font._atlases[^1]);
			(SpacingScale, AtlasScale, LAtlasScale) = (height / 120f, height / Atlas.Size, height / LargestAtlas.Size);
			(Spacing, LineSpacing) = ((spacing ?? font.Spacing) * SpacingScale, lineSpacing ?? font.LineSpacing);
		}
	}

	public void Dispose() {
		foreach (var tex in _textureAtlases) tex.Dispose();
		_textureAtlases.Clear();
		_atlases.Clear();
	}
}