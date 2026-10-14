using AchievementDrift.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class SafetyRulesTests {
	private static StoreInfo Store(params int[] categories) => new(true, "Juego", [.. categories]);

	[TestMethod]
	public void BlacklistWinsOverAllowlist() {
		DriftConfig config = new() { Blacklist = [10], Allowlist = [10] };

		Assert.AreEqual(EEligibility.Blacklisted, SafetyRules.Evaluate(10, config, Store(22)));
	}

	[TestMethod]
	public void AllowlistBypassesUnknownOnlineAndNoAchievements() {
		DriftConfig config = new() { Allowlist = [10] };

		Assert.AreEqual(EEligibility.Eligible, SafetyRules.Evaluate(10, config, null));
		Assert.AreEqual(EEligibility.Eligible, SafetyRules.Evaluate(10, config, new StoreInfo(false, "", [])));
		Assert.AreEqual(EEligibility.Eligible, SafetyRules.Evaluate(10, config, Store(1, 22)));
		Assert.AreEqual(EEligibility.Eligible, SafetyRules.Evaluate(10, config, Store(2)));
	}

	[TestMethod]
	public void VacWinsOverAllowlistWhenExcludeVac() {
		Assert.AreEqual(EEligibility.Vac, SafetyRules.Evaluate(10, new DriftConfig { Allowlist = [10] }, Store(8, 1, 22)));
		Assert.AreEqual(EEligibility.Eligible, SafetyRules.Evaluate(10, new DriftConfig { Allowlist = [10], ExcludeVac = false }, Store(8, 1, 22)));
	}

	[TestMethod]
	public void NeedsStoreInfoForAllowlistOnlyWhenExcludeVac() {
		Assert.IsTrue(SafetyRules.NeedsStoreInfo(10, new DriftConfig { Allowlist = [10] }));
		Assert.IsFalse(SafetyRules.NeedsStoreInfo(10, new DriftConfig { Allowlist = [10], ExcludeVac = false }));
	}

	[TestMethod]
	public void NeedsStoreInfoIsFalseForBlacklist() {
		Assert.IsFalse(SafetyRules.NeedsStoreInfo(10, new DriftConfig { Blacklist = [10] }));
		Assert.IsFalse(SafetyRules.NeedsStoreInfo(10, new DriftConfig { Blacklist = [10], Allowlist = [10] }));
		Assert.IsTrue(SafetyRules.NeedsStoreInfo(10, new DriftConfig { ExcludeVac = false }));
	}

	[TestMethod]
	public void MissingOrUnknownStoreInfoIsUnknown() {
		DriftConfig config = new();

		Assert.AreEqual(EEligibility.Unknown, SafetyRules.Evaluate(10, config, null));
		Assert.AreEqual(EEligibility.Unknown, SafetyRules.Evaluate(10, config, new StoreInfo(false, "", [])));
		Assert.IsTrue(SafetyRules.NeedsStoreInfo(10, config));
	}

	[TestMethod]
	public void VacIsExcluded() {
		Assert.AreEqual(EEligibility.Vac, SafetyRules.Evaluate(10, new DriftConfig(), Store(8, 22)));
		Assert.AreEqual(EEligibility.Eligible, SafetyRules.Evaluate(10, new DriftConfig { ExcludeVac = false }, Store(8, 22)));
	}

	[TestMethod]
	[DataRow(1)]
	[DataRow(20)]
	[DataRow(27)]
	[DataRow(36)]
	[DataRow(38)]
	[DataRow(49)]
	public void OnlineIsExcluded(int category) {
		Assert.AreEqual(EEligibility.Online, SafetyRules.Evaluate(10, new DriftConfig(), Store(category, 22)));
		Assert.AreEqual(EEligibility.Eligible, SafetyRules.Evaluate(10, new DriftConfig { ExcludeOnline = false }, Store(category, 22)));
	}

	[TestMethod]
	public void GamesWithoutAchievementsAreExcluded() {
		Assert.AreEqual(EEligibility.NoAchievements, SafetyRules.Evaluate(10, new DriftConfig(), Store(2)));
	}

	[TestMethod]
	public void SinglePlayerWithAchievementsIsEligible() {
		Assert.AreEqual(EEligibility.Eligible, SafetyRules.Evaluate(10, new DriftConfig(), Store(2, 22)));
	}
}
