using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AchievementDrift.Core;

internal static class StatusBuilder {
	// Ruta del panel web (IWebInterface.WebPath + barra final: sin ella ASF-ui intercepta la URL)
	internal const string PanelPath = "/drift/";

	internal const string StateActive = "Active";
	internal const string StatePaused = "Paused";

	internal static DriftStatus Build(string botName, bool connected, DriftConfig config, ActiveWindow window, BotState state, DateTime nowUtc) {
		ArgumentNullException.ThrowIfNull(botName);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(window);
		ArgumentNullException.ThrowIfNull(state);

		// Los planes completados se guardan (para no volver a planificar el juego) pero ya no se muestran
		List<GameStatus> games = state.Plans.Values
			.Where(static plan => !plan.IsCompleted)
			.Select(plan => BuildGame(plan, state.ActiveGames.Contains(plan.AppId), window, nowUtc))
			.OrderBy(static game => game.State == StateActive ? 0 : 1)
			.ThenBy(static game => game.AppId)
			.ToList();

		return new DriftStatus(botName, connected, config.Enabled, config.DryRun, window.Contains(nowUtc), config.ActiveHours, config.DescribeTimeZone(), config.MaxActiveGames, games);
	}

	internal static string FormatPlan(GamePlan plan, CultureInfo? culture = null) {
		ArgumentNullException.ThrowIfNull(plan);

		StringBuilder text = new();
		text.AppendLine(Texts.FormatFor(culture, ETextKey.PlanHeader, plan.GameName, plan.AppId, Hours(plan.ActiveMinutes), Hours(plan.PlanMinutes)));

		foreach (PlannedAchievement entry in plan.Entries.OrderBy(static e => e.OffsetMinutes)) {
			string mark = entry.State switch {
				EEntryState.Unlocked => "✅",
				EEntryState.Simulated => "🧪",
				EEntryState.Skipped => "⛔",
				_ => "⏳"
			};

			text.AppendLine(Texts.FormatFor(culture, ETextKey.PlanEntry, mark, entry.DisplayName, entry.GlobalPercent, Hours(entry.OffsetMinutes)));
		}

		return text.ToString().TrimEnd();
	}

	// withPanel = false para respuestas de varios bots: la línea del panel se añade una sola vez al final
	internal static string FormatText(DriftStatus status, CultureInfo? culture = null, bool withPanel = true) {
		ArgumentNullException.ThrowIfNull(status);

		StringBuilder text = new();
		text.AppendLine(
			Texts.FormatFor(
				culture,
				ETextKey.StatusHeader,
				Texts.Get(status.Enabled ? ETextKey.StatusEnabled : ETextKey.StatusDisabled, culture),
				status.DryRun ? Texts.Get(ETextKey.StatusDryRun, culture) : "",
				status.ActiveHours,
				status.TimeZone,
				Texts.Get(status.InActiveWindow ? ETextKey.StatusInside : ETextKey.StatusOutside, culture)
			)
		);

		if (status.Games.Count == 0) {
			text.AppendLine(Texts.Get(ETextKey.StatusNoPlans, culture));
		}

		foreach (GameStatus game in status.Games) {
			string state = game.State == StateActive ? "▶️" : "⏸️";

			text.Append(Texts.FormatFor(culture, ETextKey.StatusGame, state, game.Name, game.Done, game.Total, game.ProgressPercent, game.DaysSinceStart));

			if (game.Next != null) {
				text.Append(Texts.FormatFor(culture, ETextKey.StatusNext, game.Next.Name, game.Next.GlobalPercent));

				if (game.Next.EtaUtc is DateTime eta) {
					text.Append(CultureInfo.InvariantCulture, $" ~{eta:yyyy-MM-dd HH:mm} UTC");
				}
			}

			text.AppendLine();
		}

		if (!withPanel) {
			return text.ToString().TrimEnd();
		}

		text.Append(PanelLine(culture));

		return text.ToString();
	}

	internal static string PanelLine(CultureInfo? culture = null) => Texts.FormatFor(culture, ETextKey.StatusPanel, PanelPath);

	private static GameStatus BuildGame(GamePlan plan, bool isActive, ActiveWindow window, DateTime nowUtc) {
		string state = isActive ? StateActive : StatePaused;
		int done = plan.Entries.Count(static e => e.State is EEntryState.Unlocked or EEntryState.Simulated);
		int skipped = plan.Entries.Count(static e => e.State == EEntryState.Skipped);
		long planMinutes = plan.PlanMinutes;
		double progress = (planMinutes == 0) || plan.IsCompleted ? 100 : Math.Min(100, Math.Round(plan.ActiveMinutes * 100.0 / planMinutes, 1));

		PlannedAchievement? next = plan.GetNextPending();
		NextAchievementStatus? nextStatus = next == null ? null : new NextAchievementStatus(next.DisplayName, next.GlobalPercent, state == StateActive ? window.EstimateUtc(nowUtc, next.OffsetMinutes - plan.ActiveMinutes) : null);

		return new GameStatus(plan.AppId, plan.GameName, state, done, skipped, plan.Entries.Count, progress, Hours(plan.ActiveMinutes), Hours(planMinutes), (int) (nowUtc - plan.CreatedAtUtc).TotalDays, nextStatus);
	}

	private static double Hours(long minutes) => Math.Round(minutes / 60.0, 1);
}
