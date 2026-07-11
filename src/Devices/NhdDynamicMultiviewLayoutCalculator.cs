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
	/// Two layout categories are supported:
	/// - No presentation source: all tiles are equal size, arranged in a near-square grid, ordered
	///   by priority (row-major, lowest priority number first).
	/// - Active presentation source: the presentation is always tile 1 and is larger than the other
	///   tiles; all participant tiles are equal size to each other and arranged in a strip alongside
	///   the presentation tile, ordered by priority.
	/// </summary>
	public static class NhdDynamicMultiviewLayoutCalculator
	{
		/// <summary>
		/// Fraction of the canvas width occupied by the presentation tile when a presentation
		/// source is active. The remaining width is divided evenly among the participant tiles.
		/// </summary>
		public const double PresentationTileWidthFraction = 0.7;

		private const int DefaultCanvasWidth = 1920;
		private const int DefaultCanvasHeight = 1080;

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

			var stripX = Math.Max(1, (int)Math.Round(canvasWidth * PresentationTileWidthFraction, MidpointRounding.AwayFromZero));
			var stripWidth = Math.Max(1, canvasWidth - stripX);
			var tileHeight = Math.Max(1, canvasHeight / participants.Count);

			for (var i = 0; i < participants.Count; i++)
			{
				var y = i * tileHeight;
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

		private static IEnumerable<NhdMultiviewTileState> BuildEvenGrid(
			IReadOnlyList<MultiviewParticipantSource> participants,
			int canvasWidth,
			int canvasHeight)
		{
			if (participants == null || participants.Count == 0)
				yield break;

			var count = participants.Count;
			var cols = (int)Math.Ceiling(Math.Sqrt(count));
			var rows = (int)Math.Ceiling((double)count / cols);

			var cellWidth = Math.Max(1, canvasWidth / cols);
			var cellHeight = Math.Max(1, canvasHeight / rows);

			for (var i = 0; i < count; i++)
			{
				var row = i / cols;
				var col = i % cols;

				var x = col * cellWidth;
				var y = row * cellHeight;

				// The true last column/row absorbs any rounding remainder so tiles cover the full
				// canvas. An incomplete last row simply stops before reaching the last column,
				// leaving the remaining space empty (left-aligned).
				var width = col == cols - 1 ? Math.Max(1, canvasWidth - x) : cellWidth;
				var height = row == rows - 1 ? Math.Max(1, canvasHeight - y) : cellHeight;

				yield return new NhdMultiviewTileState(
					i + 1,
					participants[i].SourceKey,
					x,
					y,
					width,
					height,
					NhdMultiviewScaleMode.Fit.ToString());
			}
		}
	}
}
