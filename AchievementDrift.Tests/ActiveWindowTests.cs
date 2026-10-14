using System;
using System.Collections.Generic;
using AchievementDrift.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class ActiveWindowTests {
	private static ActiveWindow Utc(string hours) {
		Assert.IsTrue(ActiveWindow.TryParseHours(hours, out TimeOnly start, out TimeOnly end));

		return new ActiveWindow(start, end, TimeZoneInfo.Utc);
	}

	private static DateTime At(int year, int month, int day, int hour, int minute) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

	[TestMethod]
	[DataRow("10:00-01:00", 900)]
	[DataRow("9:30-17:45", 495)]
	[DataRow("00:00-00:00", 1440)]
	public void MinutesPerDay(string hours, int expected) => Assert.AreEqual(expected, Utc(hours).MinutesPerDay);

	[TestMethod]
	[DataRow(23, 0, true)]
	[DataRow(0, 30, true)]
	[DataRow(1, 0, false)]
	[DataRow(9, 59, false)]
	[DataRow(10, 0, true)]
	public void ContainsHandlesMidnightWrap(int hour, int minute, bool expected) {
		Assert.AreEqual(expected, Utc("10:00-01:00").Contains(At(2026, 1, 1, hour, minute)));
	}

	[TestMethod]
	public void FullDayWindowAlwaysContains() => Assert.IsTrue(Utc("00:00-00:00").Contains(At(2026, 1, 1, 4, 0)));

	[TestMethod]
	public void ContainsUsesTimeZone() {
		ActiveWindow window = ActiveWindow.FromConfig(new DriftConfig { ActiveHours = "10:00-11:00", TimeZone = "America/Bogota" });

		Assert.IsTrue(window.Contains(At(2026, 1, 1, 15, 30)));
		Assert.IsFalse(window.Contains(At(2026, 1, 1, 10, 30)));
	}

	[TestMethod]
	public void ContainsHandlesDaylightSaving() {
		ActiveWindow window = ActiveWindow.FromConfig(new DriftConfig { ActiveHours = "10:00-11:00", TimeZone = "America/New_York" });

		Assert.IsTrue(window.Contains(At(2026, 7, 1, 14, 30)));
		Assert.IsTrue(window.Contains(At(2026, 1, 15, 15, 30)));
		Assert.IsFalse(window.Contains(At(2026, 1, 15, 14, 30)));
	}

	[TestMethod]
	public void EstimateSkipsTimeOutsideWindow() {
		DateTime? eta = Utc("10:00-11:00").EstimateUtc(At(2026, 1, 1, 10, 59), 2);

		Assert.AreEqual(At(2026, 1, 2, 10, 1), eta);
	}

	[TestMethod]
	public void EstimateWithZeroMinutesReturnsNow() {
		DateTime now = At(2026, 1, 1, 12, 0);

		Assert.AreEqual(now, Utc("10:00-11:00").EstimateUtc(now, 0));
	}

	[TestMethod]
	public void EstimateManyMatchesEstimateUtc() {
		ActiveWindow window = Utc("10:00-11:00");
		DateTime from = At(2026, 1, 1, 10, 30);
		long[] minutes = [0, 1, 29, 30, 31, 95];

		IReadOnlyList<DateTime?> many = window.EstimateManyUtc(from, minutes);

		Assert.AreEqual(minutes.Length, many.Count);

		for (int i = 0; i < minutes.Length; i++) {
			Assert.AreEqual(window.EstimateUtc(from, minutes[i]), many[i], $"minutes {minutes[i]}");
		}
	}

	[TestMethod]
	public void EstimateManyRequiresAscendingOrder() {
		Assert.ThrowsExactly<ArgumentException>(() => Utc("10:00-11:00").EstimateManyUtc(At(2026, 1, 1, 10, 0), [5, 3]));
	}
}
