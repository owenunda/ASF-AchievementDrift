using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace AchievementDrift.Core;

public sealed record DriftConfig {
	public const int MaxAllowedActiveGames = 10;
	public const int MaxAllowedDays = 365;

	public bool Enabled { get; init; } = true;
	public bool DryRun { get; init; } = true;
	public int MinDays { get; init; } = 30;
	public int MaxDays { get; init; } = 45;
	public string ActiveHours { get; init; } = "10:00-01:00";
	public string? TimeZone { get; init; }
	public int MaxActiveGames { get; init; } = 2;
	public bool ExcludeVac { get; init; } = true;
	public bool ExcludeOnline { get; init; } = true;
	public ImmutableHashSet<uint> Blacklist { get; init; } = [];
	public ImmutableHashSet<uint> Allowlist { get; init; } = [];

	public string DescribeTimeZone(CultureInfo? culture = null) => TimeZone ?? Texts.FormatFor(culture, ETextKey.TimeZoneServer, TimeZones.LocalId);

	public IReadOnlyList<string> Validate(CultureInfo? culture = null) {
		List<string> errors = [];

		if (MinDays is < 1 or > MaxAllowedDays) {
			errors.Add(Texts.FormatFor(culture, ETextKey.ValMinDays, nameof(MinDays), MaxAllowedDays));
		}

		if ((MaxDays < MinDays) || (MaxDays > MaxAllowedDays)) {
			errors.Add(Texts.FormatFor(culture, ETextKey.ValMaxDays, nameof(MaxDays), nameof(MinDays), MaxAllowedDays));
		}

		if (!ActiveWindow.TryParseHours(ActiveHours, out _, out _)) {
			errors.Add(Texts.FormatFor(culture, ETextKey.ValActiveHours, nameof(ActiveHours)));
		}

		if ((TimeZone != null) && !TimeZones.IsValid(TimeZone)) {
			errors.Add(TimeZones.Available ? Texts.FormatFor(culture, ETextKey.ValTimeZone, nameof(TimeZone), TimeZone) : Texts.FormatFor(culture, ETextKey.ValTimeZoneUnavailable, nameof(TimeZone)));
		}

		if (MaxActiveGames is < 1 or > MaxAllowedActiveGames) {
			errors.Add(Texts.FormatFor(culture, ETextKey.ValMaxActiveGames, nameof(MaxActiveGames), MaxAllowedActiveGames));
		}

		// El JSON de la IPC no impone la nulabilidad: una lista null llega aquí tal cual
		if (Blacklist is null) {
			errors.Add(Texts.FormatFor(culture, ETextKey.ValListNull, nameof(Blacklist)));
		}

		if (Allowlist is null) {
			errors.Add(Texts.FormatFor(culture, ETextKey.ValListNull, nameof(Allowlist)));
		}

		if ((Blacklist?.Contains(0) == true) || (Allowlist?.Contains(0) == true)) {
			errors.Add(Texts.Get(ETextKey.ValListZero, culture));
		}

		return errors;
	}
}
