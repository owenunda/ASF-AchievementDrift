using System;
using System.Linq;
using AchievementDrift.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class StatusBuilderTests {
	private static readonly DateTime Now = new(2026, 1, 11, 12, 0, 0, DateTimeKind.Utc);
	private static readonly ActiveWindow AlwaysOn = new(new TimeOnly(0, 0), new TimeOnly(0, 0), TimeZoneInfo.Utc);

	private static GamePlan Plan(uint appId, long activeMinutes, params (long Offset, EEntryState State)[] entries) => new() {
		AppId = appId,
		GameName = $"Juego {appId}",
		CreatedAtUtc = Now.AddDays(-10),
		ActiveMinutes = activeMinutes,
		Entries = entries.Select((e, i) => new PlannedAchievement { ApiName = $"A{i}", DisplayName = $"Logro {i}", GlobalPercent = 12.5, OffsetMinutes = e.Offset, State = e.State }).ToList()
	};

	private static BotState SampleState() => new() {
		Plans = {
			[1] = Plan(1, 60, (30, EEntryState.Unlocked), (120, EEntryState.Pending)),
			[2] = Plan(2, 10, (100, EEntryState.Pending)),
			[3] = Plan(3, 500, (50, EEntryState.Unlocked), (60, EEntryState.Skipped))
		},
		ActiveGames = { 1 }
	};

	[TestMethod]
	public void GamesAreOrderedActivePausedCompleted() {
		DriftStatus status = StatusBuilder.Build("bot", true, new DriftConfig(), AlwaysOn, SampleState(), Now);

		CollectionAssert.AreEqual(new[] { "Active", "Paused", "Completed" }, status.Games.Select(static g => g.State).ToArray());
	}

	[TestMethod]
	public void ActiveGameHasProgressAndEta() {
		GameStatus game = StatusBuilder.Build("bot", true, new DriftConfig(), AlwaysOn, SampleState(), Now).Games[0];

		Assert.AreEqual(1, game.Done);
		Assert.AreEqual(2, game.Total);
		Assert.AreEqual(50, game.ProgressPercent);
		Assert.AreEqual(1, game.PlayedHours);
		Assert.AreEqual(2, game.PlanHours);
		Assert.AreEqual(10, game.DaysSinceStart);
		Assert.IsNotNull(game.Next);
		Assert.AreEqual("Logro 1", game.Next.Name);
		Assert.AreEqual(Now.AddMinutes(60), game.Next.EtaUtc);
	}

	[TestMethod]
	public void PausedGameHasNoEta() {
		GameStatus game = StatusBuilder.Build("bot", true, new DriftConfig(), AlwaysOn, SampleState(), Now).Games[1];

		Assert.IsNotNull(game.Next);
		Assert.IsNull(game.Next.EtaUtc);
	}

	[TestMethod]
	public void CompletedGameCountsSkipped() {
		GameStatus game = StatusBuilder.Build("bot", true, new DriftConfig(), AlwaysOn, SampleState(), Now).Games[2];

		Assert.AreEqual(1, game.Done);
		Assert.AreEqual(1, game.Skipped);
		Assert.IsNull(game.Next);
		Assert.AreEqual(100, game.ProgressPercent);
	}

	[TestMethod]
	public void TextMentionsBotAndGames() {
		string text = StatusBuilder.FormatText(StatusBuilder.Build("manticora", true, new DriftConfig(), AlwaysOn, SampleState(), Now));

		StringAssert.Contains(text, "Juego 1");
		StringAssert.Contains(text, "DryRun");
	}

	[TestMethod]
	public void TextEndsWithPanelPath() {
		string withGames = StatusBuilder.FormatText(StatusBuilder.Build("manticora", true, new DriftConfig(), AlwaysOn, SampleState(), Now));
		string empty = StatusBuilder.FormatText(StatusBuilder.Build("manticora", true, new DriftConfig(), AlwaysOn, new BotState(), Now));

		foreach (string text in new[] { withGames, empty }) {
			string lastLine = text.Split('\n')[^1];

			StringAssert.Contains(lastLine, StatusBuilder.PanelPath);
		}

		Assert.AreEqual("/drift/", StatusBuilder.PanelPath);
	}

	[TestMethod]
	public void PlanTextListsEntries() {
		string text = StatusBuilder.FormatPlan(SampleState().Plans[1]);

		StringAssert.Contains(text, "Logro 0");
		StringAssert.Contains(text, "Logro 1");
	}
}
