using System;
using System.Collections.Generic;
using System.Linq;
using AchievementDrift.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class PlanGeneratorTests {
	private const int Window = 900;
	private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
	private static readonly DriftConfig Fixed30 = new() { MinDays = 30, MaxDays = 30 };

	private static List<AchievementInfo> Items(params double[] percents) => percents.Select((p, i) => new AchievementInfo($"ACH_{i}", $"Logro {i}", p)).ToList();

	[TestMethod]
	public void NeverFinishesBeforeMinDays() {
		for (int seed = 0; seed < 200; seed++) {
			List<AchievementInfo> items = Items(90, 70, 50, 20, 5, 1);
			GamePlan plan = PlanGenerator.Create(1, "Juego", items, items.Count, Fixed30, Window, new Random(seed), Now);

			Assert.IsTrue(plan.PlanMinutes >= 30L * Window, $"seed {seed}: {plan.PlanMinutes}");
		}
	}

	[TestMethod]
	public void TotalStaysCloseToBudget() {
		List<AchievementInfo> items = Items(90, 70, 50, 20, 5, 1);
		GamePlan plan = PlanGenerator.Create(1, "Juego", items, items.Count, Fixed30, Window, new Random(7), Now);

		long budget = 30L * Window;
		Assert.IsTrue(plan.PlanMinutes <= budget + (items.Count * (PlanGenerator.MinGapMinutes + 1)));
	}

	[TestMethod]
	public void GapsRespectMinimum() {
		List<AchievementInfo> items = Items(Enumerable.Repeat(99.0, 200).ToArray());
		GamePlan plan = PlanGenerator.Create(1, "Juego", items, items.Count, new DriftConfig { MinDays = 1, MaxDays = 1 }, 60, new Random(3), Now);

		long previous = 0;

		foreach (PlannedAchievement entry in plan.Entries.OrderBy(static e => e.OffsetMinutes)) {
			Assert.IsTrue(entry.OffsetMinutes - previous >= PlanGenerator.MinGapMinutes);
			previous = entry.OffsetMinutes;
		}
	}

	[TestMethod]
	public void RareAchievementsTendToComeLast() {
		int rareLast = 0;

		for (int seed = 0; seed < 1000; seed++) {
			GamePlan plan = PlanGenerator.Create(1, "Juego", [new AchievementInfo("COMMON", "Común", 80), new AchievementInfo("RARE", "Raro", 2)], 2, Fixed30, Window, new Random(seed), Now);

			long common = plan.Entries.Single(static e => e.ApiName == "COMMON").OffsetMinutes;
			long rare = plan.Entries.Single(static e => e.ApiName == "RARE").OffsetMinutes;

			if (rare > common) {
				rareLast++;
			}
		}

		Assert.IsTrue(rareLast > 900, $"rareLast = {rareLast}");
	}

	[TestMethod]
	public void RareAchievementsWaitLonger() {
		double commonGaps = 0;
		double rareGaps = 0;

		for (int seed = 0; seed < 500; seed++) {
			GamePlan plan = PlanGenerator.Create(1, "Juego", [new AchievementInfo("COMMON", "Común", 80), new AchievementInfo("RARE", "Raro", 2)], 2, Fixed30, Window, new Random(seed), Now);
			List<PlannedAchievement> ordered = plan.Entries.OrderBy(static e => e.OffsetMinutes).ToList();
			long first = ordered[0].OffsetMinutes;
			long second = ordered[1].OffsetMinutes - ordered[0].OffsetMinutes;

			if (ordered[0].ApiName == "RARE") {
				rareGaps += first;
				commonGaps += second;
			} else {
				commonGaps += first;
				rareGaps += second;
			}
		}

		Assert.IsTrue(rareGaps > commonGaps * 3, $"rare {rareGaps} vs common {commonGaps}");
	}

	[TestMethod]
	public void BudgetIsProportionalToPendingAchievements() {
		DriftConfig config = new() { MinDays = 30, MaxDays = 30 };

		Assert.AreEqual(30L * Window / 10, PlanGenerator.ComputeBudget(5, 50, config, Window, new Random(1)));
		Assert.AreEqual(30L * Window, PlanGenerator.ComputeBudget(50, 50, config, Window, new Random(1)));
	}

	[TestMethod]
	public void BudgetStaysWithinDayRange() {
		DriftConfig config = new() { MinDays = 30, MaxDays = 45 };

		for (int seed = 0; seed < 100; seed++) {
			long budget = PlanGenerator.ComputeBudget(10, 10, config, Window, new Random(seed));

			Assert.IsTrue(budget >= 30L * Window && budget <= 45L * Window);
		}
	}

	[TestMethod]
	public void PercentsAreNormalized() {
		Assert.AreEqual(PlanGenerator.DefaultPercent, PlanGenerator.NormalizePercent(double.NaN));
		Assert.AreEqual(PlanGenerator.MinPercent, PlanGenerator.NormalizePercent(0));
		Assert.AreEqual(100, PlanGenerator.NormalizePercent(250));
		Assert.AreEqual(12.5, PlanGenerator.NormalizePercent(12.5));

		GamePlan plan = PlanGenerator.Create(1, "Juego", Items(double.NaN), 1, Fixed30, Window, new Random(1), Now);
		Assert.AreEqual(PlanGenerator.DefaultPercent, plan.Entries[0].GlobalPercent);
	}

	[TestMethod]
	public void EmptyPendingProducesCompletedPlan() {
		GamePlan plan = PlanGenerator.Create(1, "Juego", [], 10, Fixed30, Window, new Random(1), Now);

		Assert.AreEqual(0, plan.Entries.Count);
		Assert.IsTrue(plan.IsCompleted);
	}

	[TestMethod]
	public void CreateFillsMetadata() {
		GamePlan plan = PlanGenerator.Create(440, "Team Fortress 2", Items(50), 1, Fixed30 with { DryRun = true }, Window, new Random(1), Now);

		Assert.AreEqual(440u, plan.AppId);
		Assert.AreEqual("Team Fortress 2", plan.GameName);
		Assert.AreEqual(Now, plan.CreatedAtUtc);
		Assert.IsTrue(plan.CreatedInDryRun);
		Assert.IsTrue(plan.Entries.TrueForAll(static e => e.State == EEntryState.Pending));
	}

	[TestMethod]
	public void AppendSchedulesAfterExistingPlan() {
		GamePlan plan = PlanGenerator.Create(1, "Juego", Items(50, 40), 2, Fixed30, Window, new Random(1), Now);
		long previousEnd = plan.PlanMinutes;

		PlanGenerator.Append(plan, Items(30), 3, Fixed30, Window, new Random(2));

		Assert.AreEqual(3, plan.Entries.Count);
		Assert.IsTrue(plan.Entries[2].OffsetMinutes > previousEnd);
	}

	[TestMethod]
	public void AppendStartsFromActiveMinutesWhenAhead() {
		GamePlan plan = PlanGenerator.Create(1, "Juego", Items(50), 1, Fixed30, Window, new Random(1), Now);
		plan.ActiveMinutes = plan.PlanMinutes + 1000;

		PlanGenerator.Append(plan, Items(30), 2, Fixed30, Window, new Random(2));

		Assert.IsTrue(plan.Entries[1].OffsetMinutes > plan.ActiveMinutes);
	}

	[TestMethod]
	public void GetNextPendingReturnsLowestOffset() {
		GamePlan plan = PlanGenerator.Create(1, "Juego", Items(50, 40, 30), 3, Fixed30, Window, new Random(1), Now);
		PlannedAchievement first = plan.Entries.MinBy(static e => e.OffsetMinutes)!;
		first.State = EEntryState.Unlocked;

		PlannedAchievement? next = plan.GetNextPending();

		Assert.IsNotNull(next);
		Assert.AreEqual(plan.Entries.Where(static e => e.State == EEntryState.Pending).Min(static e => e.OffsetMinutes), next.OffsetMinutes);
	}
}
