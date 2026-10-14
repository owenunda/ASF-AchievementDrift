using System;
using System.Collections.Immutable;
using System.Globalization;
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

	internal static string Describe(EEligibility eligibility, CultureInfo? culture = null) => Texts.Get(
		eligibility switch {
			EEligibility.Eligible => ETextKey.EligEligible,
			EEligibility.Unknown => ETextKey.EligUnknown,
			EEligibility.Blacklisted => ETextKey.EligBlacklisted,
			EEligibility.Vac => ETextKey.EligVac,
			EEligibility.Online => ETextKey.EligOnline,
			EEligibility.NoAchievements => ETextKey.EligNoAchievements,
			_ => throw new ArgumentOutOfRangeException(nameof(eligibility))
		},
		culture
	);

	internal static EEligibility Evaluate(uint appId, DriftConfig config, StoreInfo? info) {
		ArgumentNullException.ThrowIfNull(config);

		if (config.Blacklist.Contains(appId)) {
			return EEligibility.Blacklisted;
		}

		// VAC gana a Allowlist: con ExcludeVac un juego VAC nunca se toca, aunque esté en Allowlist
		if (config.ExcludeVac && info is { Found: true } && info.Categories.Contains(VacCategory)) {
			return EEligibility.Vac;
		}

		// Allowlist solo salta las comprobaciones de tienda desconocida, online y sin logros
		if (config.Allowlist.Contains(appId)) {
			return EEligibility.Eligible;
		}

		if (info is not { Found: true }) {
			return EEligibility.Unknown;
		}

		if (config.ExcludeOnline && info.Categories.Any(OnlineCategories.Contains)) {
			return EEligibility.Online;
		}

		return info.Categories.Contains(AchievementsCategory) ? EEligibility.Eligible : EEligibility.NoAchievements;
	}

	internal static bool NeedsStoreInfo(uint appId, DriftConfig config) {
		ArgumentNullException.ThrowIfNull(config);

		if (config.Blacklist.Contains(appId)) {
			return false;
		}

		// Para un juego en Allowlist solo hace falta la tienda si hay que comprobar VAC
		return config.ExcludeVac || !config.Allowlist.Contains(appId);
	}
}
