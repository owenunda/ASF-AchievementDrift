using System;
using System.Collections.Generic;
using System.Linq;
using AchievementDrift.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class DriftEngineTests {
	private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
	private static readonly ActiveWindow AlwaysOn = new(new TimeOnly(0, 0), new TimeOnly(0, 0), TimeZoneInfo.Utc);
	private static readonly ActiveWindow NeverNow = new(new TimeOnly(1, 0), new TimeOnly(2, 0), TimeZoneInfo.Utc);
	private static readonly DriftConfig Config = new() { DryRun = false, MaxActiveGames = 2 };
	private static readonly HashSet<uint> NoCooldown = [];

	private static GamePlan Plan(uint appId, long activeMinutes, params long[] offsets) => new() {
		AppId = appId,
		GameName = $"Juego {appId}",
		ActiveMinutes = activeMinutes,
		Entries = offsets.Select((offset, i) => new PlannedAchievement { ApiName = $"A{appId}_{i}", DisplayName = $"Logro {i}", GlobalPercent = 50, OffsetMinutes = offset }).ToList()
	};

	private static DriftEngine Engine() => new(new Random(42));

	[TestMethod]
	public void OutsideWindowDoesNothing() {
		BotState state = new() { Plans = { [10] = Plan(10, 0, 100) }, ActiveGames = { 10 } };

		TickResult result = Engine().Tick(state, Now, [10], NoCooldown, Config, NeverNow);

		Assert.AreEqual(0, state.Plans[10].ActiveMinutes);
		Assert.IsNull(result.Unlock);
	}

	[TestMethod]
	public void DisabledDoesNothing() {
		BotState state = new() { Plans = { [10] = Plan(10, 0, 100) }, ActiveGames = { 10 } };

		Engine().Tick(state, Now, [10], NoCooldown, Config with { Enabled = false }, AlwaysOn);

		Assert.AreEqual(0, state.Plans[10].ActiveMinutes);
	}

	[TestMethod]
	public void ActiveGameAccumulatesMinutes() {
		BotState state = new() { Plans = { [10] = Plan(10, 5, 100) }, ActiveGames = { 10 } };

		TickResult result = Engine().Tick(state, Now, [10], NoCooldown, Config, AlwaysOn);

		Assert.AreEqual(6, state.Plans[10].ActiveMinutes);
		Assert.IsTrue(result.StateChanged);
	}

	[TestMethod]
	public void StoppedGameIsPausedAndFreesSlot() {
		BotState state = new() { Plans = { [10] = Plan(10, 5, 100) }, ActiveGames = { 10 } };

		Engine().Tick(state, Now, [], NoCooldown, Config, AlwaysOn);

		Assert.AreEqual(5, state.Plans[10].ActiveMinutes);
		Assert.AreEqual(0, state.ActiveGames.Count);
		Assert.IsTrue(state.Plans.ContainsKey(10));
	}

	[TestMethod]
	public void RespectsMaxActiveGames() {
		BotState state = new();

		TickResult result = Engine().Tick(state, Now, [1, 2, 3, 4, 5], NoCooldown, Config, AlwaysOn);

		Assert.AreEqual(2, state.ActiveGames.Count);
		CollectionAssert.AreEquivalent(state.ActiveGames, result.GamesNeedingPlan.ToList());
	}

	[TestMethod]
	public void PrefersStartedPlansWhenFillingSlots() {
		BotState state = new() { Plans = { [3] = Plan(3, 50, 100), [5] = Plan(5, 20, 100) } };

		Engine().Tick(state, Now, [1, 2, 3, 4, 5], NoCooldown, Config, AlwaysOn);

		CollectionAssert.AreEquivalent(new List<uint> { 3, 5 }, state.ActiveGames);
	}

	[TestMethod]
	public void ShrinksWhenMaxActiveGamesIsReduced() {
		BotState state = new() { Plans = { [1] = Plan(1, 0, 100), [2] = Plan(2, 0, 100) }, ActiveGames = { 1, 2 } };

		Engine().Tick(state, Now, [1, 2], NoCooldown, Config with { MaxActiveGames = 1 }, AlwaysOn);

		Assert.AreEqual(1, state.ActiveGames.Count);
	}

	[TestMethod]
	public void CompletedGamesNeverTakeSlots() {
		GamePlan done = Plan(1, 500, 100);
		done.Entries[0].State = EEntryState.Unlocked;
		BotState state = new() { Plans = { [1] = done }, ActiveGames = { 1 } };

		Engine().Tick(state, Now, [1], NoCooldown, Config, AlwaysOn);

		Assert.AreEqual(0, state.ActiveGames.Count);
	}

	[TestMethod]
	public void ReturnsAtMostOneDueUnlockWithLowestOffset() {
		BotState state = new() { Plans = { [1] = Plan(1, 1000, 300, 200), [2] = Plan(2, 1000, 250) }, ActiveGames = { 1, 2 } };

		TickResult result = Engine().Tick(state, Now, [1, 2], NoCooldown, Config, AlwaysOn);

		Assert.IsNotNull(result.Unlock);
		Assert.AreEqual(1u, result.Unlock.AppId);
		Assert.AreEqual(200, result.Unlock.Entry.OffsetMinutes);
	}

	[TestMethod]
	public void NotDueYetReturnsNoUnlock() {
		BotState state = new() { Plans = { [1] = Plan(1, 10, 100) }, ActiveGames = { 1 } };

		TickResult result = Engine().Tick(state, Now, [1], NoCooldown, Config, AlwaysOn);

		Assert.IsNull(result.Unlock);
	}

	[TestMethod]
	public void DryRunPlansAreDroppedWhenSwitchingToRealMode() {
		GamePlan simulated = Plan(1, 10, 100);
		simulated.CreatedInDryRun = true;
		BotState state = new() { Plans = { [1] = simulated }, ActiveGames = { 1 } };

		TickResult result = Engine().Tick(state, Now, [1], NoCooldown, Config with { DryRun = false }, AlwaysOn);

		Assert.IsFalse(state.Plans.ContainsKey(1));
		CollectionAssert.Contains(result.GamesNeedingPlan.ToList(), 1u);
	}

	[TestMethod]
	public void RealPlanWithSimulatedEntriesIsDroppedWhenSwitchingToRealMode() {
		GamePlan plan = Plan(1, 200, 100, 300);
		plan.Entries[0].State = EEntryState.Simulated;
		BotState state = new() { Plans = { [1] = plan }, ActiveGames = { 1 } };

		TickResult result = Engine().Tick(state, Now, [1], NoCooldown, Config with { DryRun = false }, AlwaysOn);

		Assert.IsFalse(state.Plans.ContainsKey(1));
		CollectionAssert.Contains(result.GamesNeedingPlan.ToList(), 1u);
	}

	[TestMethod]
	public void RealPlanWithSimulatedEntriesIsKeptWhileInDryRun() {
		GamePlan plan = Plan(1, 200, 100, 300);
		plan.Entries[0].State = EEntryState.Simulated;
		BotState state = new() { Plans = { [1] = plan }, ActiveGames = { 1 } };

		TickResult result = Engine().Tick(state, Now, [1], NoCooldown, Config with { DryRun = true }, AlwaysOn);

		Assert.AreSame(plan, state.Plans[1]);
		Assert.AreEqual(0, result.GamesNeedingPlan.Count);
	}

	[TestMethod]
	public void CoolingDownGameReleasesSlotToAnotherPlayingGame() {
		BotState state = new() { Plans = { [1] = Plan(1, 50, 100), [2] = Plan(2, 20, 100) }, ActiveGames = { 1 } };

		TickResult result = Engine().Tick(state, Now, [1, 2], new HashSet<uint> { 1 }, Config with { MaxActiveGames = 1 }, AlwaysOn);

		CollectionAssert.AreEqual(new List<uint> { 2 }, state.ActiveGames);
		Assert.IsTrue(state.Plans.ContainsKey(1));
		Assert.AreEqual(50, state.Plans[1].ActiveMinutes);
		Assert.AreEqual(21, state.Plans[2].ActiveMinutes);
		Assert.IsTrue(result.StateChanged);
	}

	[TestMethod]
	public void CoolingDownGameWithoutPlanDoesNotTakeSlot() {
		BotState state = new();

		TickResult result = Engine().Tick(state, Now, [1, 2], new HashSet<uint> { 1 }, Config, AlwaysOn);

		CollectionAssert.AreEqual(new List<uint> { 2 }, state.ActiveGames);
		CollectionAssert.AreEqual(new List<uint> { 2 }, result.GamesNeedingPlan.ToList());
	}

	[TestMethod]
	public void CoolingDownGameDueEntryIsSkippedWhileOtherGameIsReturned() {
		BotState state = new() { Plans = { [1] = Plan(1, 1000, 100), [2] = Plan(2, 1000, 200) }, ActiveGames = { 1, 2 } };

		TickResult result = Engine().Tick(state, Now, [1, 2], new HashSet<uint> { 1 }, Config, AlwaysOn);

		Assert.IsNotNull(result.Unlock);
		Assert.AreEqual(2u, result.Unlock.AppId);
		Assert.AreEqual(200, result.Unlock.Entry.OffsetMinutes);
	}

	[TestMethod]
	public void GameDoesNotUnlockTwiceWithinMinGap() {
		GamePlan plan = Plan(1, 1000, 100, 200);
		plan.LastUnlockActiveMinutes = 1001 - PlanGenerator.MinGapMinutes + 1;
		BotState state = new() { Plans = { [1] = plan }, ActiveGames = { 1 } };

		TickResult result = Engine().Tick(state, Now, [1], NoCooldown, Config, AlwaysOn);

		Assert.IsNull(result.Unlock);
	}

	[TestMethod]
	public void GameUnlocksAgainOnceMinGapHasPassed() {
		GamePlan plan = Plan(1, 1000, 100, 200);
		plan.LastUnlockActiveMinutes = 1001 - PlanGenerator.MinGapMinutes;
		BotState state = new() { Plans = { [1] = plan }, ActiveGames = { 1 } };

		TickResult result = Engine().Tick(state, Now, [1], NoCooldown, Config, AlwaysOn);

		Assert.IsNotNull(result.Unlock);
		Assert.AreEqual(100, result.Unlock.Entry.OffsetMinutes);
	}

	[TestMethod]
	public void GameInMinGapDoesNotBlockOtherGames() {
		GamePlan recent = Plan(1, 1000, 100);
		recent.LastUnlockActiveMinutes = 1000;
		BotState state = new() { Plans = { [1] = recent, [2] = Plan(2, 1000, 300) }, ActiveGames = { 1, 2 } };

		TickResult result = Engine().Tick(state, Now, [1, 2], NoCooldown, Config, AlwaysOn);

		Assert.IsNotNull(result.Unlock);
		Assert.AreEqual(2u, result.Unlock.AppId);
	}

	[TestMethod]
	[DataRow((int) EEntryState.Unlocked)]
	[DataRow((int) EEntryState.Simulated)]
	public void RecordUnlockStoresActiveMinutesForObtainedEntries(int stateValue) {
		EEntryState state = (EEntryState) stateValue;
		GamePlan plan = Plan(1, 345, 100);

		DriftEngine.RecordUnlock(plan, plan.Entries[0], state, Now);

		Assert.AreEqual(state, plan.Entries[0].State);
		Assert.AreEqual(Now, plan.Entries[0].DoneAtUtc);
		Assert.AreEqual(345, plan.LastUnlockActiveMinutes);
	}

	[TestMethod]
	public void RecordUnlockIgnoresSkippedEntries() {
		GamePlan plan = Plan(1, 345, 100);

		DriftEngine.RecordUnlock(plan, plan.Entries[0], EEntryState.Skipped, Now);

		Assert.AreEqual(EEntryState.Skipped, plan.Entries[0].State);
		Assert.IsNull(plan.LastUnlockActiveMinutes);
	}

	[TestMethod]
	public void ApplyResultSetsStateAndTime() {
		PlannedAchievement entry = new();

		DriftEngine.ApplyResult(entry, EEntryState.Unlocked, Now);

		Assert.AreEqual(EEntryState.Unlocked, entry.State);
		Assert.AreEqual(Now, entry.DoneAtUtc);
	}
}
