using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AchievementDrift.Core;

// Combina el estado en vivo de Steam, los porcentajes globales y el plan en la vista de logros de un juego
internal static class GameAchievementsBuilder {
	internal const string IconBaseUrl = "https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/";

	internal const string StatusNext = "Next";
	internal const string StatusProtected = "Protected";
	internal const string StatusScheduled = "Scheduled";
	internal const string StatusSimulated = "Simulated";
	internal const string StatusSkipped = "Skipped";
	internal const string StatusUnlocked = "Unlocked";
	internal const string StatusUnlockedByDrift = "UnlockedByDrift";
	internal const string StatusUnplanned = "Unplanned";

	internal static GameAchievements Build(uint appId, string gameName, IReadOnlyList<LiveAchievement>? live, IReadOnlyDictionary<string, double> percents, GamePlan? plan, bool isActive, ActiveWindow window, DateTime nowUtc) {
		ArgumentNullException.ThrowIfNull(gameName);
		ArgumentNullException.ThrowIfNull(percents);
		ArgumentNullException.ThrowIfNull(window);

		Dictionary<string, PlannedAchievement> entries = new(StringComparer.Ordinal);

		foreach (PlannedAchievement entry in plan?.Entries ?? []) {
			entries.TryAdd(entry.ApiName, entry);
		}

		Dictionary<string, DateTime?> etas = isActive && (plan != null) ? EstimatePending(plan, window, nowUtc) : [];
		PlannedAchievement? next = plan?.GetNextPending();

		IEnumerable<AchievementView> views = live != null
			? live.Select(achievement => FromLive(appId, achievement, entries.GetValueOrDefault(achievement.ApiName), next, etas, percents))
			: entries.Values.Select(entry => FromPlan(entry, next, etas, percents));

		List<AchievementView> ordered = views
			.OrderByDescending(static view => view.GlobalPercent ?? -1)
			.ThenBy(static view => view.Name, StringComparer.CurrentCultureIgnoreCase)
			.ToList();

		return new GameAchievements(appId, gameName, live != null, ordered.Count(static view => view.Unlocked), ordered.Count, ordered);
	}

	private static Dictionary<string, DateTime?> EstimatePending(GamePlan plan, ActiveWindow window, DateTime nowUtc) {
		List<PlannedAchievement> pending = plan.Entries.Where(static entry => entry.State == EEntryState.Pending).OrderBy(static entry => entry.OffsetMinutes).ToList();
		IReadOnlyList<DateTime?> etas = window.EstimateManyUtc(nowUtc, pending.Select(entry => Math.Max(0, entry.OffsetMinutes - plan.ActiveMinutes)).ToList());

		Dictionary<string, DateTime?> result = new(StringComparer.Ordinal);

		for (int i = 0; i < pending.Count; i++) {
			result.TryAdd(pending[i].ApiName, etas[i]);
		}

		return result;
	}

	private static AchievementView FromLive(uint appId, LiveAchievement achievement, PlannedAchievement? entry, PlannedAchievement? next, Dictionary<string, DateTime?> etas, IReadOnlyDictionary<string, double> percents) {
		double? percent = percents.TryGetValue(achievement.ApiName, out double value) ? value : entry?.GlobalPercent;
		string icon = achievement.IsUnlocked ? achievement.Icon : achievement.IconGray;
		Uri? iconUrl = string.IsNullOrEmpty(icon) ? null : new Uri($"{IconBaseUrl}{appId.ToString(CultureInfo.InvariantCulture)}/{Uri.EscapeDataString(icon)}");

		(string status, DateTime? atUtc) = achievement.IsUnlocked
			? (entry is { State: EEntryState.Unlocked, DoneAtUtc: not null } ? (StatusUnlockedByDrift, entry.DoneAtUtc) : (StatusUnlocked, null))
			: achievement.IsProtected
				? (StatusProtected, null)
				: entry == null
					? (StatusUnplanned, null)
					: Describe(entry, next, etas);

		return new AchievementView(achievement.ApiName, achievement.Name, achievement.Description, iconUrl, percent, achievement.IsUnlocked, status, atUtc);
	}

	private static AchievementView FromPlan(PlannedAchievement entry, PlannedAchievement? next, Dictionary<string, DateTime?> etas, IReadOnlyDictionary<string, double> percents) {
		double percent = percents.TryGetValue(entry.ApiName, out double value) ? value : entry.GlobalPercent;
		bool unlocked = entry.State == EEntryState.Unlocked;
		(string status, DateTime? atUtc) = unlocked ? (StatusUnlockedByDrift, entry.DoneAtUtc) : Describe(entry, next, etas);

		return new AchievementView(entry.ApiName, entry.DisplayName, "", null, percent, unlocked, status, atUtc);
	}

	private static (string Status, DateTime? AtUtc) Describe(PlannedAchievement entry, PlannedAchievement? next, Dictionary<string, DateTime?> etas) => entry.State switch {
		EEntryState.Unlocked => (StatusUnlockedByDrift, entry.DoneAtUtc),
		EEntryState.Simulated => (StatusSimulated, entry.DoneAtUtc),
		EEntryState.Skipped => (StatusSkipped, null),
		_ => (ReferenceEquals(entry, next) ? StatusNext : StatusScheduled, etas.GetValueOrDefault(entry.ApiName))
	};
}
