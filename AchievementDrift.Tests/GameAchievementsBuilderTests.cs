using System;
using System.Collections.Generic;
using System.Linq;
using AchievementDrift.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class GameAchievementsBuilderTests {
	private const uint AppId = 356190;
	private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
	private static readonly ActiveWindow AlwaysOn = new(new TimeOnly(0, 0), new TimeOnly(0, 0), TimeZoneInfo.Utc);

	private static LiveAchievement Live(string apiName, bool unlocked = false, bool isProtected = false) => new(apiName, $"{apiName} nombre", $"{apiName} desc", $"{apiName}.jpg", $"{apiName}_gray.jpg", unlocked, isProtected);

	private static PlannedAchievement Entry(string apiName, long offset, EEntryState state = EEntryState.Pending, DateTime? doneAt = null) => new() {
		ApiName = apiName,
		DisplayName = $"{apiName} plan",
		GlobalPercent = 33,
		OffsetMinutes = offset,
		State = state,
		DoneAtUtc = doneAt
	};

	private static GamePlan Plan(long activeMinutes, params PlannedAchievement[] entries) => new() {
		AppId = AppId,
		GameName = "Shadow of War",
		ActiveMinutes = activeMinutes,
		Entries = [.. entries]
	};

	private static readonly Dictionary<string, double> NoPercents = [];

	private static AchievementView Find(GameAchievements result, string apiName) => result.Achievements.Single(a => a.ApiName == apiName);

	[TestMethod]
	public void UnlockedByDriftShowsUnlockTime() {
		DateTime doneAt = Now.AddHours(-2);
		GameAchievements result = GameAchievementsBuilder.Build(AppId, "Shadow of War", [Live("A", unlocked: true), Live("B", unlocked: true)], NoPercents, Plan(100, Entry("A", 50, EEntryState.Unlocked, doneAt)), true, AlwaysOn, Now);

		Assert.AreEqual(GameAchievementsBuilder.StatusUnlockedByDrift, Find(result, "A").Status);
		Assert.AreEqual(doneAt, Find(result, "A").AtUtc);
		Assert.AreEqual(GameAchievementsBuilder.StatusUnlocked, Find(result, "B").Status);
		Assert.IsNull(Find(result, "B").AtUtc);
		Assert.AreEqual(2, result.Unlocked);
		Assert.AreEqual(2, result.Total);
		Assert.IsTrue(result.Live);
	}

	[TestMethod]
	public void PendingEntriesGetNextAndScheduledWithEtaWhenActive() {
		GameAchievements result = GameAchievementsBuilder.Build(AppId, "Shadow of War", [Live("A"), Live("B")], NoPercents, Plan(10, Entry("A", 30), Entry("B", 60)), true, AlwaysOn, Now);

		Assert.AreEqual(GameAchievementsBuilder.StatusNext, Find(result, "A").Status);
		Assert.AreEqual(Now.AddMinutes(20), Find(result, "A").AtUtc);
		Assert.AreEqual(GameAchievementsBuilder.StatusScheduled, Find(result, "B").Status);
		Assert.AreEqual(Now.AddMinutes(50), Find(result, "B").AtUtc);
	}

	[TestMethod]
	public void PausedGameHasNoEtas() {
		GameAchievements result = GameAchievementsBuilder.Build(AppId, "Shadow of War", [Live("A"), Live("B")], NoPercents, Plan(10, Entry("A", 30), Entry("B", 60)), false, AlwaysOn, Now);

		Assert.AreEqual(GameAchievementsBuilder.StatusNext, Find(result, "A").Status);
		Assert.IsNull(Find(result, "A").AtUtc);
		Assert.IsNull(Find(result, "B").AtUtc);
	}

	[TestMethod]
	public void ProtectedSkippedSimulatedAndUnplannedStatuses() {
		DateTime simulatedAt = Now.AddMinutes(-5);
		GameAchievements result = GameAchievementsBuilder.Build(
			AppId,
			"Shadow of War",
			[Live("P", isProtected: true), Live("S"), Live("D"), Live("U")],
			NoPercents,
			Plan(100, Entry("S", 10, EEntryState.Skipped, Now), Entry("D", 20, EEntryState.Simulated, simulatedAt)),
			true,
			AlwaysOn,
			Now
		);

		Assert.AreEqual(GameAchievementsBuilder.StatusProtected, Find(result, "P").Status);
		Assert.AreEqual(GameAchievementsBuilder.StatusSkipped, Find(result, "S").Status);
		Assert.AreEqual(GameAchievementsBuilder.StatusSimulated, Find(result, "D").Status);
		Assert.AreEqual(simulatedAt, Find(result, "D").AtUtc);
		Assert.AreEqual(GameAchievementsBuilder.StatusUnplanned, Find(result, "U").Status);
		Assert.AreEqual(0, result.Unlocked);
	}

	[TestMethod]
	public void SortedByGlobalPercentDescendingWithUnknownLast() {
		Dictionary<string, double> percents = new() { ["A"] = 10, ["B"] = 72.2 };
		GameAchievements result = GameAchievementsBuilder.Build(AppId, "Shadow of War", [Live("A"), Live("C"), Live("B")], percents, null, false, AlwaysOn, Now);

		CollectionAssert.AreEqual(new[] { "B", "A", "C" }, result.Achievements.Select(static a => a.ApiName).ToArray());
		Assert.AreEqual(72.2, Find(result, "B").GlobalPercent);
		Assert.IsNull(Find(result, "C").GlobalPercent);
	}

	[TestMethod]
	public void PlanPercentIsFallback() {
		GameAchievements result = GameAchievementsBuilder.Build(AppId, "Shadow of War", [Live("A")], NoPercents, Plan(0, Entry("A", 30)), false, AlwaysOn, Now);

		Assert.AreEqual(33, Find(result, "A").GlobalPercent);
	}

	[TestMethod]
	public void IconDependsOnUnlockState() {
		GameAchievements result = GameAchievementsBuilder.Build(AppId, "Shadow of War", [Live("A", unlocked: true), Live("B"), new LiveAchievement("C", "C", "", "", "", false, false)], NoPercents, null, false, AlwaysOn, Now);

		Assert.AreEqual($"{GameAchievementsBuilder.IconBaseUrl}{AppId}/A.jpg", Find(result, "A").IconUrl?.AbsoluteUri);
		Assert.AreEqual($"{GameAchievementsBuilder.IconBaseUrl}{AppId}/B_gray.jpg", Find(result, "B").IconUrl?.AbsoluteUri);
		Assert.IsNull(Find(result, "C").IconUrl);
		Assert.AreEqual("A desc", Find(result, "A").Description);
	}

	[TestMethod]
	public void OfflineFallsBackToPlanEntries() {
		DateTime doneAt = Now.AddHours(-1);
		GameAchievements result = GameAchievementsBuilder.Build(AppId, "Shadow of War", null, NoPercents, Plan(10, Entry("A", 5, EEntryState.Unlocked, doneAt), Entry("B", 30)), true, AlwaysOn, Now);

		Assert.IsFalse(result.Live);
		Assert.AreEqual(2, result.Total);
		Assert.AreEqual(1, result.Unlocked);
		Assert.AreEqual(GameAchievementsBuilder.StatusUnlockedByDrift, Find(result, "A").Status);
		Assert.AreEqual("A plan", Find(result, "A").Name);
		Assert.IsNull(Find(result, "A").IconUrl);
		Assert.AreEqual(GameAchievementsBuilder.StatusNext, Find(result, "B").Status);
		Assert.AreEqual(Now.AddMinutes(20), Find(result, "B").AtUtc);
	}

	[TestMethod]
	public void NoLiveDataAndNoPlanIsEmpty() {
		GameAchievements result = GameAchievementsBuilder.Build(AppId, "Juego", null, NoPercents, null, false, AlwaysOn, Now);

		Assert.AreEqual(0, result.Total);
		Assert.AreEqual("Juego", result.Name);
	}
}
