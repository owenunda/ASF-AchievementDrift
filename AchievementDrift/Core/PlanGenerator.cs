using System;
using System.Collections.Generic;
using System.Linq;

namespace AchievementDrift.Core;

internal static class PlanGenerator {
	internal const double DefaultPercent = 50;
	internal const int MinGapMinutes = 15;
	internal const double MinPercent = 0.1;

	internal static void Append(GamePlan plan, IReadOnlyList<AchievementInfo> newOnes, int totalAchievements, DriftConfig config, int windowMinutesPerDay, Random random) {
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(newOnes);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(random);

		if (newOnes.Count == 0) {
			return;
		}

		long start = Math.Max(plan.PlanMinutes, plan.ActiveMinutes);
		long budget = ComputeBudget(newOnes.Count, totalAchievements, config, windowMinutesPerDay, random);

		plan.Entries.AddRange(Schedule(newOnes, budget, start, random));
	}

	internal static long ComputeBudget(int pendingCount, int totalCount, DriftConfig config, int windowMinutesPerDay, Random random) {
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(random);

		double days = config.MinDays + (random.NextDouble() * (config.MaxDays - config.MinDays));
		double minutes = days * windowMinutesPerDay;

		if (totalCount > 0) {
			// Multiplicar antes de dividir evita errores de redondeo (27000 * 5 / 50 == 2700 exacto)
			minutes = minutes * Math.Clamp(pendingCount, 0, totalCount) / totalCount;
		}

		return (long) Math.Ceiling(minutes);
	}

	internal static GamePlan Create(uint appId, string gameName, IReadOnlyList<AchievementInfo> pending, int totalAchievements, DriftConfig config, int windowMinutesPerDay, Random random, DateTime nowUtc) {
		ArgumentNullException.ThrowIfNull(gameName);
		ArgumentNullException.ThrowIfNull(pending);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(random);

		GamePlan plan = new() {
			AppId = appId,
			GameName = gameName,
			CreatedAtUtc = nowUtc,
			CreatedInDryRun = config.DryRun
		};

		if (pending.Count > 0) {
			long budget = ComputeBudget(pending.Count, totalAchievements, config, windowMinutesPerDay, random);

			plan.Entries.AddRange(Schedule(pending, budget, 0, random));
		}

		return plan;
	}

	internal static double NormalizePercent(double percent) => double.IsNaN(percent) ? DefaultPercent : Math.Min(100, Math.Max(MinPercent, percent));

	private static List<PlannedAchievement> Schedule(IReadOnlyList<AchievementInfo> items, long budgetMinutes, long startOffset, Random random) {
		// Orden aleatorio ponderado (Efraimidis-Spirakis): clave U^(1/p), mayor primero. Los comunes tienden a salir antes.
		List<(AchievementInfo Item, double Percent)> ordered = items
			.Select(item => (Item: item, Percent: NormalizePercent(item.GlobalPercent), Key: Math.Pow(1 - random.NextDouble(), 1 / NormalizePercent(item.GlobalPercent))))
			.OrderByDescending(static entry => entry.Key)
			.Select(static entry => (entry.Item, entry.Percent))
			.ToList();

		// Hueco previo proporcional a 1/sqrt(p) con jitter, escalado para sumar el presupuesto
		double[] gaps = ordered.Select(entry => (1 / Math.Sqrt(entry.Percent)) * (0.5 + random.NextDouble())).ToArray();
		double totalGaps = 0;

		foreach (double gap in gaps) {
			totalGaps += gap;
		}

		double scale = budgetMinutes / totalGaps;

		List<PlannedAchievement> result = new(ordered.Count);
		long offset = startOffset;

		for (int i = 0; i < ordered.Count; i++) {
			offset += Math.Max(MinGapMinutes, (long) Math.Ceiling(gaps[i] * scale));

			result.Add(
				new PlannedAchievement {
					ApiName = ordered[i].Item.ApiName,
					DisplayName = ordered[i].Item.DisplayName,
					GlobalPercent = ordered[i].Percent,
					OffsetMinutes = offset,
					State = EEntryState.Pending
				}
			);
		}

		return result;
	}
}
