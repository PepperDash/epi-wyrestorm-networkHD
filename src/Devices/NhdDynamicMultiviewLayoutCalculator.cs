using System;
using System.Collections.Generic;
using System.Linq;
using PepperDash.Essentials.Core.DeviceTypeInterfaces;
using PepperDash.Essentials.Plugin.Enums;

namespace PepperDash.Essentials.Plugin
{
	/// <summary>
	/// Computes a multiview tile layout (positions/sizes per tile) at runtime from a set of
	/// participant sources with priority values, plus an optional active presentation source.
	/// This is purely a geometry calculator - it has no knowledge of hardware, protocol, or Essentials
	/// devices, and is intentionally deterministic/side-effect free so it can be unit tested in
	/// isolation. See <see cref="NhdBaseDevice.ApplyDynamicLayout"/> for the caller that sends the
	/// computed geometry to hardware.
	///
	/// For a total stream count (participants, plus the presentation tile if active) from 1 to 9,
	/// this uses the exact tile geometry reverse-engineered from the WyreStorm NHD-CTL-PRO's own
	/// <c>FormatMultiViewCommand</c> (see MultiView-Layout-Reference.md), scaled proportionally from
	/// its native 1920x1080 reference canvas to the decoder's actual output resolution - see
	/// <see cref="PresentingReferenceLayouts"/>/<see cref="NotPresentingReferenceLayouts"/>. Stream
	/// counts of 1 or 2 use the same single layout regardless of whether a presentation is active
	/// (matching the reference, which has no presenting/non-presenting split for those two cases).
	/// Stream counts outside 1-9 (e.g. a decoder supporting more than 9 simultaneous windows, which
	/// the reference doesn't cover) fall back to a generic computed layout: a near-square grid when
	/// not presenting (see <see cref="BuildEvenGrid"/>), or a large presentation tile plus a strip of
	/// equal-size participant tiles when presenting (see <see cref="BuildPresentationTile"/>/
	/// <see cref="BuildThumbnailStrip"/>).
	/// </summary>
	public static class NhdDynamicMultiviewLayoutCalculator
	{
		/// <summary>
		/// Fraction of the canvas width occupied by the presentation tile when a presentation
		/// source is active. The remaining width is divided evenly among the participant tiles.
		/// Only used by the generic fallback layout - see remarks on <see cref="CalculateLayout"/>.
		/// </summary>
		public const double PresentationTileWidthFraction = 0.7;

		/// <summary>
		/// Gap, in pixels, left between two tiles that are adjacent to one another (e.g. a narrow
		/// border), on every generated layout. Never applied between a tile and the edge of the
		/// canvas - only between adjacent tiles. Only used by the generic fallback layout - the
		/// reference layouts (1-9 total streams) already have their own gaps baked into their exact
		/// tile geometry.
		/// </summary>
		public const int TileGapPx = 8;

		/// <summary>
		/// Fallback canvas width, in pixels, used when the decoder's actual output resolution isn't
		/// known yet. Also reused by <see cref="NhdBaseDevice.CurrentLayout"/> for the same fallback,
		/// and as the reference canvas width the WyreStorm layouts below are scaled from.
		/// </summary>
		public const int DefaultCanvasWidth = 1920;

		/// <summary>
		/// Fallback canvas height, in pixels, used when the decoder's actual output resolution isn't
		/// known yet. Also reused by <see cref="NhdBaseDevice.CurrentLayout"/> for the same fallback,
		/// and as the reference canvas height the WyreStorm layouts below are scaled from.
		/// </summary>
		public const int DefaultCanvasHeight = 1080;

		/// <summary>
		/// Exact tile geometry (X, Y, width, height, at the 1920x1080 reference canvas size) for every
		/// total stream count (1-9) with NO presentation active, keyed by total stream count. Extracted
		/// verbatim from the WyreStorm NHD-CTL-PRO's <c>FormatMultiViewCommand</c> - see
		/// MultiView-Layout-Reference.md. For counts 7-9, this is the same 3x3 grid of 633x353 cells;
		/// the original command blanks unused cells with a null stream rather than omitting them, but
		/// since this calculator only ever needs geometry for actual tiles, those blanked cells are
		/// simply absent here (7 entries for count 7, 8 for count 8).
		/// </summary>
		private static readonly IReadOnlyDictionary<int, (int X, int Y, int W, int H)[]> NotPresentingReferenceLayouts =
			new Dictionary<int, (int X, int Y, int W, int H)[]>
			{
				[1] = new[] { (0, 0, 1920, 1080) },
				[2] = new[] { (0, 273, 955, 535), (965, 273, 955, 535) },
				[3] = new[] { (483, 0, 955, 535), (0, 545, 955, 535), (965, 545, 955, 535) },
				[4] = new[] { (0, 0, 955, 535), (965, 0, 955, 535), (0, 545, 955, 535), (965, 545, 955, 535) },
				[5] = new[] { (0, 113, 1445, 813), (1455, 0, 465, 262), (1455, 272, 465, 262), (1455, 546, 465, 262), (1455, 818, 465, 262) },
				[6] = new[] { (1, 107, 1539, 866), (1550, 0, 370, 208), (1550, 218, 370, 208), (1550, 436, 370, 208), (1550, 654, 370, 208), (1550, 872, 370, 208) },
				[7] = new[] { (0, 0, 633, 353), (643, 0, 633, 353), (1286, 0, 633, 353), (0, 363, 633, 353), (643, 363, 633, 353), (1286, 363, 633, 353), (0, 726, 633, 353) },
				[8] = new[] { (0, 0, 633, 353), (643, 0, 633, 353), (1286, 0, 633, 353), (0, 363, 633, 353), (643, 363, 633, 353), (1286, 363, 633, 353), (0, 726, 633, 353), (643, 726, 633, 353) },
				[9] = new[] { (0, 0, 633, 353), (643, 0, 633, 353), (1286, 0, 633, 353), (0, 363, 633, 353), (643, 363, 633, 353), (1286, 363, 633, 353), (0, 726, 633, 353), (643, 726, 633, 353), (1286, 726, 633, 353) },
			};

		/// <summary>
		/// Exact tile geometry (X, Y, width, height, at the 1920x1080 reference canvas size) for every
		/// total stream count (1-9) WITH a presentation active, keyed by total stream count (the
		/// presentation tile counts as one stream - entry 0 in each array is always the presentation
		/// tile). Extracted verbatim from the WyreStorm NHD-CTL-PRO's <c>FormatMultiViewCommand</c> -
		/// see MultiView-Layout-Reference.md. Counts 1 and 2 reuse the same single layout as
		/// <see cref="NotPresentingReferenceLayouts"/> - the reference has no presenting/non-presenting
		/// split for those two cases. Counts 7 and 8 have the presentation tile's X at 0 rather than
		/// 192 (used everywhere else) - this is exactly what the reference documents, flagged there as
		/// unconfirmed/possibly unintentional, and preserved as-is here rather than "corrected".
		/// </summary>
		private static readonly IReadOnlyDictionary<int, (int X, int Y, int W, int H)[]> PresentingReferenceLayouts =
			new Dictionary<int, (int X, int Y, int W, int H)[]>
			{
				[1] = new[] { (0, 0, 1920, 1080) },
				[2] = new[] { (0, 273, 955, 535), (965, 273, 955, 535) },
				[3] = new[] { (192, 212, 1536, 866), (595, 0, 360, 202), (965, 0, 360, 202) },
				[4] = new[] { (192, 212, 1536, 866), (410, 0, 360, 202), (780, 0, 360, 202), (1150, 0, 360, 202) },
				[5] = new[] { (192, 212, 1536, 866), (225, 0, 360, 202), (595, 0, 360, 202), (965, 0, 360, 202), (1335, 0, 360, 202) },
				[6] = new[] { (192, 212, 1536, 866), (40, 0, 360, 202), (410, 0, 360, 202), (780, 0, 360, 202), (1150, 0, 360, 202), (1520, 0, 360, 202) },
				[7] = new[] { (0, 212, 1536, 866), (1549, 212, 360, 202), (1549, 433, 360, 202), (1549, 655, 360, 202), (1549, 877, 360, 202), (403, 0, 360, 202), (773, 0, 360, 202) },
				[8] = new[] { (0, 212, 1536, 866), (1549, 212, 360, 202), (1549, 433, 360, 202), (1549, 655, 360, 202), (1549, 877, 360, 202), (218, 0, 360, 202), (588, 0, 360, 202), (958, 0, 360, 202) },
				[9] = new[] { (192, 212, 1536, 866), (1549, 212, 360, 202), (1549, 433, 360, 202), (1549, 655, 360, 202), (1549, 877, 360, 202), (33, 0, 360, 202), (403, 0, 360, 202), (773, 0, 360, 202), (1143, 0, 360, 202) },
			};

		/// <summary>
		/// Computes the tile layout for the given participant sources and optional presentation source.
		/// </summary>
		/// <param name="participantSources">Sources to place in participant tiles, each with a priority.</param>
		/// <param name="presentationSourceKey">Source key for the active presentation, or null/empty if no presentation is active.</param>
		/// <param name="canvasWidth">Output width in pixels to lay out against. Falls back to 1920 if &lt;= 0.</param>
		/// <param name="canvasHeight">Output height in pixels to lay out against. Falls back to 1080 if &lt;= 0.</param>
		/// <param name="maxTileCount">Maximum number of tiles the decoder supports. Lowest-priority participants are dropped if the requested set exceeds capacity.</param>
		/// <returns>Computed tile states, ordered by tile number (1-based).</returns>
		public static IReadOnlyList<NhdMultiviewTileState> CalculateLayout(
			IReadOnlyList<MultiviewParticipantSource> participantSources,
			string presentationSourceKey,
			int canvasWidth,
			int canvasHeight,
			int maxTileCount)
		{
			var effectiveCanvasWidth = canvasWidth > 0 ? canvasWidth : DefaultCanvasWidth;
			var effectiveCanvasHeight = canvasHeight > 0 ? canvasHeight : DefaultCanvasHeight;
			var effectiveMaxTileCount = Math.Max(0, maxTileCount);

			var hasPresentation = !string.IsNullOrWhiteSpace(presentationSourceKey);
			var availableParticipantTiles = hasPresentation
				? Math.Max(0, effectiveMaxTileCount - 1)
				: effectiveMaxTileCount;

			var orderedParticipants = (participantSources ?? Array.Empty<MultiviewParticipantSource>())
				.Where(p => p != null && !string.IsNullOrWhiteSpace(p.SourceKey))
				.OrderBy(p => p.Priority)
				.Take(availableParticipantTiles)
				.ToList();

			var totalStreams = hasPresentation ? orderedParticipants.Count + 1 : orderedParticipants.Count;
			if (totalStreams == 0)
				return new List<NhdMultiviewTileState>();

			var referenceLayouts = hasPresentation ? PresentingReferenceLayouts : NotPresentingReferenceLayouts;
			if (referenceLayouts.TryGetValue(totalStreams, out var referenceTiles))
			{
				return BuildFromReferenceLayout(
					referenceTiles,
					hasPresentation,
					presentationSourceKey,
					orderedParticipants,
					effectiveCanvasWidth,
					effectiveCanvasHeight);
			}

			// Outside the reference's documented 1-9 stream range - fall back to a generic computed
			// layout (see class remarks).
			var tiles = new List<NhdMultiviewTileState>();

			if (hasPresentation)
			{
				if (effectiveMaxTileCount > 0)
				{
					tiles.Add(BuildPresentationTile(presentationSourceKey, effectiveCanvasWidth, effectiveCanvasHeight));
				}

				tiles.AddRange(BuildThumbnailStrip(orderedParticipants, effectiveCanvasWidth, effectiveCanvasHeight, startTileNumber: 2));
			}
			else
			{
				tiles.AddRange(BuildEvenGrid(orderedParticipants, effectiveCanvasWidth, effectiveCanvasHeight));
			}

			return tiles;
		}

		/// <summary>
		/// Builds tiles from a reference layout entry, scaling each tile's geometry proportionally
		/// from the reference's native 1920x1080 canvas to the actual output resolution. The first
		/// entry is the presentation tile when <paramref name="hasPresentation"/> is true; every other
		/// entry is assigned to the next participant in priority order.
		/// </summary>
		private static IReadOnlyList<NhdMultiviewTileState> BuildFromReferenceLayout(
			(int X, int Y, int W, int H)[] referenceTiles,
			bool hasPresentation,
			string presentationSourceKey,
			IReadOnlyList<MultiviewParticipantSource> orderedParticipants,
			int canvasWidth,
			int canvasHeight)
		{
			var tiles = new List<NhdMultiviewTileState>(referenceTiles.Length);
			var participantIndex = 0;

			for (var i = 0; i < referenceTiles.Length; i++)
			{
				var sourceKey = hasPresentation && i == 0
					? presentationSourceKey
					: orderedParticipants[participantIndex++].SourceKey;

				var reference = referenceTiles[i];
				tiles.Add(new NhdMultiviewTileState(
					i + 1,
					sourceKey,
					ScaleReferenceValue(reference.X, canvasWidth, DefaultCanvasWidth),
					ScaleReferenceValue(reference.Y, canvasHeight, DefaultCanvasHeight),
					ScaleReferenceValue(reference.W, canvasWidth, DefaultCanvasWidth),
					ScaleReferenceValue(reference.H, canvasHeight, DefaultCanvasHeight),
					NhdMultiviewScaleMode.Stretch.ToString()));
			}

			return tiles;
		}

		/// <summary>
		/// Scales a reference-layout coordinate/dimension from its native reference canvas size to the
		/// actual output canvas size, preserving proportions.
		/// </summary>
		private static int ScaleReferenceValue(int referenceValue, int actualCanvasSize, int referenceCanvasSize)
		{
			return Math.Max(0, (int)Math.Round(referenceValue * (actualCanvasSize / (double)referenceCanvasSize), MidpointRounding.AwayFromZero));
		}

		private static NhdMultiviewTileState BuildPresentationTile(string sourceKey, int canvasWidth, int canvasHeight)
		{
			var width = Math.Max(1, (int)Math.Round(canvasWidth * PresentationTileWidthFraction, MidpointRounding.AwayFromZero));
			return new NhdMultiviewTileState(1, sourceKey, 0, 0, width, canvasHeight, NhdMultiviewScaleMode.Fit.ToString());
		}

		private static IEnumerable<NhdMultiviewTileState> BuildThumbnailStrip(
			IReadOnlyList<MultiviewParticipantSource> participants,
			int canvasWidth,
			int canvasHeight,
			int startTileNumber)
		{
			if (participants == null || participants.Count == 0)
				yield break;

			// Gap before the strip so there's a narrow border between the presentation tile and
			// the first thumbnail - the strip's own left edge is not the canvas edge (its right
			// edge is), so shrinking it towards the presentation tile (rather than growing the
			// presentation tile itself) keeps the presentation tile's own 0.7-fraction width exact.
			var stripX = Math.Max(1, (int)Math.Round(canvasWidth * PresentationTileWidthFraction, MidpointRounding.AwayFromZero)) + TileGapPx;
			var stripWidth = Math.Max(1, canvasWidth - stripX);
			var totalRowGaps = Math.Max(0, participants.Count - 1) * TileGapPx;
			var tileHeight = Math.Max(1, (canvasHeight - totalRowGaps) / participants.Count);

			for (var i = 0; i < participants.Count; i++)
			{
				var y = i * (tileHeight + TileGapPx);
				// Last tile absorbs any rounding remainder so the strip covers the full canvas height.
				var height = i == participants.Count - 1 ? Math.Max(1, canvasHeight - y) : tileHeight;

				yield return new NhdMultiviewTileState(
					startTileNumber + i,
					participants[i].SourceKey,
					stripX,
					y,
					stripWidth,
					height,
					NhdMultiviewScaleMode.Fit.ToString());
			}
		}

		/// <summary>
		/// Builds a 2-row layout for exactly 3 equal-size participant tiles: tile 1 alone,
		/// horizontally centered, on the top row; tiles 2 and 3 side by side on the bottom row.
		/// </summary>
		private static IEnumerable<NhdMultiviewTileState> BuildEvenGrid(
			IReadOnlyList<MultiviewParticipantSource> participants,
			int canvasWidth,
			int canvasHeight)
		{
			if (participants == null || participants.Count == 0)
				yield break;

			var count = participants.Count;
			var cols = (int)Math.Ceiling(Math.Sqrt(count));
			var fullRows = count / cols;
			var remainder = count % cols;
			var totalRows = remainder > 0 ? fullRows + 1 : fullRows;

			var totalColGaps = Math.Max(0, cols - 1) * TileGapPx;
			var totalRowGaps = Math.Max(0, totalRows - 1) * TileGapPx;
			var cellWidth = Math.Max(1, (canvasWidth - totalColGaps) / cols);
			var cellHeight = Math.Max(1, (canvasHeight - totalRowGaps) / totalRows);

			var participantIndex = 0;

			// When the tile count doesn't divide evenly into `cols` columns, the leftover tiles
			// form their own row, centered as a block, placed above the full rows below it -
			// e.g. 3 tiles => 1 (centered) over 2; 5 tiles => 2 (centered) over 3; 7 tiles => 1
			// (centered) over 3 over 3. Perfectly divisible counts (e.g. 4, 6, 9) have no such row
			// and form a plain, uncentered grid.
			if (remainder > 0)
			{
				var blockWidth = remainder * cellWidth + Math.Max(0, remainder - 1) * TileGapPx;
				var blockStartX = Math.Max(0, (canvasWidth - blockWidth) / 2);

				for (var col = 0; col < remainder; col++)
				{
					var x = blockStartX + col * (cellWidth + TileGapPx);
					// The last tile in the centered block absorbs any rounding remainder so the
					// block's own tiles are contiguous, without pushing the block off-center.
					var width = col == remainder - 1 ? Math.Max(1, blockStartX + blockWidth - x) : cellWidth;

					yield return new NhdMultiviewTileState(
						participantIndex + 1,
						participants[participantIndex].SourceKey,
						x,
						0,
						width,
						cellHeight,
						NhdMultiviewScaleMode.Fit.ToString());

					participantIndex++;
				}
			}

			var startRow = remainder > 0 ? 1 : 0;
			for (var row = startRow; row < totalRows; row++)
			{
				for (var col = 0; col < cols && participantIndex < count; col++)
				{
					var x = col * (cellWidth + TileGapPx);
					var y = row * (cellHeight + TileGapPx);

					// The true last column/row absorbs any rounding remainder so tiles cover the
					// full canvas.
					var width = col == cols - 1 ? Math.Max(1, canvasWidth - x) : cellWidth;
					var height = row == totalRows - 1 ? Math.Max(1, canvasHeight - y) : cellHeight;

					yield return new NhdMultiviewTileState(
						participantIndex + 1,
						participants[participantIndex].SourceKey,
						x,
						y,
						width,
						height,
						NhdMultiviewScaleMode.Fit.ToString());

					participantIndex++;
				}
			}
		}
	}
}
