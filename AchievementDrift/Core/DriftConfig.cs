using System;
using System.Collections.Generic;
using System.Collections.Immutable;

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

	public string DescribeTimeZone() => TimeZone ?? $"{TimeZoneInfo.Local.Id} (servidor)";

	public TimeZoneInfo ResolveTimeZone() => TimeZoneResolver.TryFind(TimeZone, out TimeZoneInfo? timeZone) ? timeZone : TimeZoneInfo.Local;

	public IReadOnlyList<string> Validate() {
		List<string> errors = [];

		if (MinDays is < 1 or > MaxAllowedDays) {
			errors.Add($"{nameof(MinDays)} debe estar entre 1 y {MaxAllowedDays}.");
		}

		if ((MaxDays < MinDays) || (MaxDays > MaxAllowedDays)) {
			errors.Add($"{nameof(MaxDays)} debe estar entre {nameof(MinDays)} y {MaxAllowedDays}.");
		}

		if (!ActiveWindow.TryParseHours(ActiveHours, out _, out _)) {
			errors.Add($"{nameof(ActiveHours)} debe tener el formato HH:mm-HH:mm (por ejemplo 10:00-01:00).");
		}

		if ((TimeZone != null) && !TimeZoneResolver.TryFind(TimeZone, out _)) {
			errors.Add($"{nameof(TimeZone)} '{TimeZone}' no es una zona horaria válida (usa un identificador IANA, por ejemplo America/Bogota).");
		}

		if (MaxActiveGames is < 1 or > MaxAllowedActiveGames) {
			errors.Add($"{nameof(MaxActiveGames)} debe estar entre 1 y {MaxAllowedActiveGames}.");
		}

		if (Blacklist.Contains(0) || Allowlist.Contains(0)) {
			errors.Add("Las listas no pueden contener el appid 0.");
		}

		return errors;
	}
}
