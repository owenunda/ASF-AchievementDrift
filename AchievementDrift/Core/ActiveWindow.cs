using System;
using System.Globalization;

namespace AchievementDrift.Core;

internal sealed class ActiveWindow {
	internal const int MinutesInDay = 24 * 60;
	private const int MaxEstimateMinutes = 400 * MinutesInDay;
	private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];

	internal TimeOnly Start { get; }
	internal TimeOnly End { get; }
	internal TimeZoneInfo TimeZone { get; }

	internal int MinutesPerDay => Start == End ? MinutesInDay : (int) (End - Start).TotalMinutes;

	internal ActiveWindow(TimeOnly start, TimeOnly end, TimeZoneInfo timeZone) {
		ArgumentNullException.ThrowIfNull(timeZone);

		Start = start;
		End = end;
		TimeZone = timeZone;
	}

	internal static ActiveWindow FromConfig(DriftConfig config) {
		ArgumentNullException.ThrowIfNull(config);

		if (!TryParseHours(config.ActiveHours, out TimeOnly start, out TimeOnly end)) {
			throw new ArgumentException(config.ActiveHours, nameof(config));
		}

		return new ActiveWindow(start, end, config.ResolveTimeZone());
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

		DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), TimeZone);

		return TimeOnly.FromDateTime(local).IsBetween(Start, End);
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
