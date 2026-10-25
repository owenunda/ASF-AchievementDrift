using System;
using System.Collections.Generic;
using System.Globalization;

namespace AchievementDrift.Core;

internal sealed class ActiveWindow {
	internal const int MinutesInDay = 24 * 60;
	private const int MaxEstimateMinutes = 400 * MinutesInDay;
	private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];

	internal TimeOnly Start { get; }
	internal TimeOnly End { get; }
	private readonly Func<DateTime, DateTime> ToLocal;

	internal int MinutesPerDay => Start == End ? MinutesInDay : (int) (End - Start).TotalMinutes;

	// toLocal: de UTC a la hora local en la que se interpreta la franja (ver TimeZones)
	internal ActiveWindow(TimeOnly start, TimeOnly end, Func<DateTime, DateTime> toLocal) {
		ArgumentNullException.ThrowIfNull(toLocal);

		Start = start;
		End = end;
		ToLocal = toLocal;
	}

	internal static ActiveWindow FromConfig(DriftConfig config) {
		ArgumentNullException.ThrowIfNull(config);

		if (!TryParseHours(config.ActiveHours, out TimeOnly start, out TimeOnly end)) {
			throw new ArgumentException(config.ActiveHours, nameof(config));
		}

		return new ActiveWindow(start, end, TimeZones.ToLocal(config.TimeZone));
	}

	internal static bool TryParseHours(string? text, out TimeOnly start, out TimeOnly end) {
		start = end = default;

		if (string.IsNullOrWhiteSpace(text)) {
			return false;
		}

		string[] parts = text.Split('-');

		return (parts.Length == 2) &&
			TimeOnly.TryParseExact(parts[0].Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out start) &&
			TimeOnly.TryParseExact(parts[1].Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out end);
	}

	internal bool Contains(DateTime utcNow) {
		if (Start == End) {
			return true;
		}

		DateTime local = ToLocal(utcNow);

		return TimeOnly.FromDateTime(local).IsBetween(Start, End);
	}

	// Igual que EstimateUtc para varios valores ascendentes a la vez, recorriendo la franja una sola vez
	internal IReadOnlyList<DateTime?> EstimateManyUtc(DateTime fromUtc, IReadOnlyList<long> ascendingActiveMinutes) {
		ArgumentNullException.ThrowIfNull(ascendingActiveMinutes);

		DateTime?[] result = new DateTime?[ascendingActiveMinutes.Count];
		DateTime cursor = new(fromUtc.Ticks - (fromUtc.Ticks % TimeSpan.TicksPerMinute), DateTimeKind.Utc);
		long elapsed = 0;
		int index = 0;

		for (int i = 0; i < ascendingActiveMinutes.Count; i++) {
			if ((i > 0) && (ascendingActiveMinutes[i] < ascendingActiveMinutes[i - 1])) {
				throw new ArgumentException("Los minutos deben estar en orden ascendente.", nameof(ascendingActiveMinutes));
			}
		}

		while ((index < result.Length) && (ascendingActiveMinutes[index] <= 0)) {
			result[index++] = fromUtc;
		}

		for (int step = 0; (step < MaxEstimateMinutes) && (index < result.Length); step++) {
			cursor = cursor.AddMinutes(1);

			if (!Contains(cursor)) {
				continue;
			}

			elapsed++;

			while ((index < result.Length) && (ascendingActiveMinutes[index] == elapsed)) {
				result[index++] = cursor;
			}
		}

		return result;
	}

	// Instante UTC en el que se habrán acumulado activeMinutes minutos dentro de la franja, o null si excede el límite
	internal DateTime? EstimateUtc(DateTime fromUtc, long activeMinutes) {
		if (activeMinutes <= 0) {
			return fromUtc;
		}

		DateTime cursor = new(fromUtc.Ticks - (fromUtc.Ticks % TimeSpan.TicksPerMinute), DateTimeKind.Utc);
		long remaining = activeMinutes;

		for (int i = 0; i < MaxEstimateMinutes; i++) {
			cursor = cursor.AddMinutes(1);

			if (Contains(cursor) && (--remaining == 0)) {
				return cursor;
			}
		}

		return null;
	}
}
