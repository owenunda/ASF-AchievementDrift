using System;
using System.Diagnostics.CodeAnalysis;

namespace AchievementDrift.Core;

internal static class TimeZoneResolver {
	internal static bool TryFind(string? id, [NotNullWhen(true)] out TimeZoneInfo? timeZone) {
		timeZone = null;

		if (string.IsNullOrWhiteSpace(id)) {
			return false;
		}

		try {
			timeZone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());

			return true;
		} catch (TimeZoneNotFoundException) {
			return false;
		} catch (InvalidTimeZoneException) {
			return false;
		}
	}
}
