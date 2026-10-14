using System;
using System.Collections.Immutable;
using System.Linq;

namespace AchievementDrift.Core;

internal enum EEligibility : byte {
	Eligible,
	Unknown,
	Blacklisted,
	Vac,
	Online,
	NoAchievements
}

internal static class SafetyRules {
	internal const int AchievementsCategory = 22;
	internal const int VacCategory = 8;

	// Multi-player, MMO, Cross-Platform Multiplayer, Online PvP, Online Co-op, PvP
	internal static readonly ImmutableHashSet<int> OnlineCategories = [1, 20, 27, 36, 38, 49];

	internal static string Describe(EEligibility eligibility) => eligibility switch {
		EEligibility.Eligible => "elegible",
		EEligibility.Unknown => "sin datos de tienda (añádelo a Allowlist si es seguro)",
		EEligibility.Blacklisted => "en Blacklist",
		EEligibility.Vac => "tiene VAC",
		EEligibility.Online => "es online/multijugador",
		EEligibility.NoAchievements => "no tiene logros",
		_ => throw new ArgumentOutOfRangeException(nameof(eligibility))
	};

	internal static EEligibility Evaluate(uint appId, DriftConfig config, StoreInfo? info) {
		ArgumentNullException.ThrowIfNull(config);

		if (config.Blacklist.Contains(appId)) {
			return EEligibility.Blacklisted;
		}

		if (config.Allowlist.Contains(appId)) {
			return EEligibility.Eligible;
		}

		if (info is not { Found: true }) {
			return EEligibility.Unknown;
		}

		if (config.ExcludeVac && info.Categories.Contains(VacCategory)) {
			return EEligibility.Vac;
		}

		if (config.ExcludeOnline && info.Categories.Any(OnlineCategories.Contains)) {
			return EEligibility.Online;
		}

		return info.Categories.Contains(AchievementsCategory) ? EEligibility.Eligible : EEligibility.NoAchievements;
	}

	internal static bool NeedsStoreInfo(uint appId, DriftConfig config) {
		ArgumentNullException.ThrowIfNull(config);

		return !config.Blacklist.Contains(appId) && !config.Allowlist.Contains(appId);
	}
}
