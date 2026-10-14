using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using AchievementDrift.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class DriftConfigTests {
	[TestMethod]
	public void DefaultsAreValid() {
		DriftConfig config = new();

		Assert.AreEqual(0, config.Validate().Count);
		Assert.IsTrue(config.Enabled);
		Assert.IsTrue(config.DryRun);
		Assert.AreEqual(30, config.MinDays);
		Assert.AreEqual(45, config.MaxDays);
		Assert.AreEqual("10:00-01:00", config.ActiveHours);
		Assert.IsNull(config.TimeZone);
		Assert.AreEqual(2, config.MaxActiveGames);
		Assert.IsTrue(config.ExcludeVac);
		Assert.IsTrue(config.ExcludeOnline);
	}

	[TestMethod]
	[DataRow(0, 45)]
	[DataRow(366, 366)]
	[DataRow(30, 29)]
	[DataRow(30, 366)]
	public void InvalidDaysAreRejected(int minDays, int maxDays) {
		DriftConfig config = new() { MinDays = minDays, MaxDays = maxDays };

		Assert.IsTrue(config.Validate().Count > 0);
	}

	[TestMethod]
	[DataRow("25:00-01:00")]
	[DataRow("10:00")]
	[DataRow("abc")]
	[DataRow("")]
	public void InvalidHoursAreRejected(string hours) {
		DriftConfig config = new() { ActiveHours = hours };

		Assert.IsTrue(config.Validate().Count > 0);
	}

	[TestMethod]
	public void ValidationMessagesFollowLanguage() {
		DriftConfig config = new() { MinDays = 0 };

		StringAssert.Contains(config.Validate(new CultureInfo("en-US"))[0], "must be between");
		StringAssert.Contains(config.Validate(new CultureInfo("es-ES"))[0], "debe estar entre");
	}

	[TestMethod]
	public void InvalidTimeZoneIsRejected() {
		DriftConfig config = new() { TimeZone = "Mars/Phobos" };

		Assert.IsTrue(config.Validate().Count > 0);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(11)]
	public void InvalidMaxActiveGamesIsRejected(int value) {
		DriftConfig config = new() { MaxActiveGames = value };

		Assert.IsTrue(config.Validate().Count > 0);
	}

	[TestMethod]
	public void ZeroAppIdInListsIsRejected() {
		Assert.IsTrue(new DriftConfig { Blacklist = [0] }.Validate().Count > 0);
		Assert.IsTrue(new DriftConfig { Allowlist = [0] }.Validate().Count > 0);
	}

	[TestMethod]
	public void NullListsAreRejectedWithoutThrowing() {
		IReadOnlyList<string> blacklistErrors = new DriftConfig { Blacklist = null! }.Validate();
		IReadOnlyList<string> allowlistErrors = new DriftConfig { Allowlist = null! }.Validate();

		Assert.IsTrue(blacklistErrors.Any(static error => error.Contains("Blacklist", StringComparison.Ordinal)));
		Assert.IsTrue(allowlistErrors.Any(static error => error.Contains("Allowlist", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void ResolveTimeZoneFallsBackToLocal() {
		Assert.AreEqual(TimeZoneInfo.Local.Id, new DriftConfig().ResolveTimeZone().Id);
	}

	[TestMethod]
	public void ResolveTimeZoneUsesIanaId() {
		TimeZoneInfo timeZone = new DriftConfig { TimeZone = "America/Bogota" }.ResolveTimeZone();

		Assert.AreEqual(TimeSpan.FromHours(-5), timeZone.BaseUtcOffset);
	}
}
