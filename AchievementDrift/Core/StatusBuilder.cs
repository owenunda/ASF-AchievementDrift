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
	internal const string StateCompleted = "Completed";
	internal const string StatePaused = "Paused";

	internal static DriftStatus Build(string botName, bool connected, DriftConfig config, ActiveWindow window, BotState state, DateTime nowUtc) {
		ArgumentNullException.ThrowIfNull(botName);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(window);
		ArgumentNullException.ThrowIfNull(state);

		List<GameStatus> games = state.Plans.Values
			.Select(plan => BuildGame(plan, state.ActiveGames.Contains(plan.AppId), window, nowUtc))
			.OrderBy(static game => game.State switch { StateActive => 0, StatePaused => 1, _ => 2 })
			.ThenBy(static game => game.AppId)
			.ToList();

		return new DriftStatus(botName, connected, config.Enabled, config.DryRun, window.Contains(nowUtc), config.ActiveHours, config.DescribeTimeZone(), config.MaxActiveGames, games);
	}

	internal static string FormatPlan(GamePlan plan) {
		ArgumentNullException.ThrowIfNull(plan);

		StringBuilder text = new();
		text.AppendLine(CultureInfo.InvariantCulture, $"Plan de {plan.GameName} ({plan.AppId}) — {Hours(plan.ActiveMinutes)} h jugadas de {Hours(plan.PlanMinutes)} h:");

		foreach (PlannedAchievement entry in plan.Entries.OrderBy(static e => e.OffsetMinutes)) {
			string mark = entry.State switch {
				EEntryState.Unlocked => "✅",
				EEntryState.Simulated => "🧪",
				EEntryState.Skipped => "⛔",
				_ => "⏳"
			};

			text.AppendLine(CultureInfo.InvariantCulture, $"{mark} {entry.DisplayName} ({entry.GlobalPercent:0.#}%) — a las {Hours(entry.OffsetMinutes)} h");
		}

		return text.ToString().TrimEnd();
	}

	internal static string FormatText(DriftStatus status) {
		ArgumentNullException.ThrowIfNull(status);

		StringBuilder text = new();
		text.AppendLine(CultureInfo.InvariantCulture, $"AchievementDrift: {(status.Enabled ? "activado" : "desactivado")}{(status.DryRun ? " · DryRun (simulación)" : "")} · horario {status.ActiveHours} ({status.TimeZone}) · {(status.InActiveWindow ? "dentro" : "fuera")} del horario");

		if (status.Games.Count == 0) {
			text.AppendLine("Todavía no hay planes. Se crean cuando el bot juega un juego elegible dentro del horario.");
			text.Append(PanelLine);

			return text.ToString();
		}

		foreach (GameStatus game in status.Games) {
			string state = game.State switch {
				StateActive => "▶️",
				StatePaused => "⏸️",
				_ => "🏁"
			};

			text.Append(CultureInfo.InvariantCulture, $"{state} {game.Name}: {game.Done}/{game.Total} ({game.ProgressPercent:0}% del plan, día {game.DaysSinceStart})");

			if (game.Next != null) {
				text.Append(CultureInfo.InvariantCulture, $" · siguiente: {game.Next.Name} ({game.Next.GlobalPercent:0.#}%)");

				if (game.Next.EtaUtc is DateTime eta) {
					text.Append(CultureInfo.InvariantCulture, $" ~{eta:yyyy-MM-dd HH:mm} UTC");
				}
			}

			text.AppendLine();
		}

		text.Append(PanelLine);

		return text.ToString();
	}

	private const string PanelLine = $"Panel web: {PanelPath} (añádelo a la dirección de tu ASF, con la barra final)";

	private static GameStatus BuildGame(GamePlan plan, bool isActive, ActiveWindow window, DateTime nowUtc) {
		string state = plan.IsCompleted ? StateCompleted : isActive ? StateActive : StatePaused;
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
