using System;
using System.Globalization;
using System.Threading.Tasks;
using AchievementDrift.Core;
using AchievementDrift.Runtime;
using ArchiSteamFarm.Steam;

namespace AchievementDrift.Commands;

internal static class DriftCommands {
	private const string Usage = "Uso: !drift status | on | off | plan <appid> | replan <appid>";

	internal static async Task<string?> HandleAsync(Bot bot, EAccess access, string[] args) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(args);

		if ((args.Length == 0) || !args[0].Equals("drift", StringComparison.OrdinalIgnoreCase) || (access < EAccess.Master)) {
			return null;
		}

		string subcommand = args.Length > 1 ? args[1].ToUpperInvariant() : "STATUS";
		uint appId = 0;
		bool hasAppId = (args.Length > 2) && uint.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out appId) && (appId > 0);

		string response = subcommand switch {
			"STATUS" => StatusBuilder.FormatText(await DriftRuntime.GetStatusAsync(bot).ConfigureAwait(false)),
			"ON" => await SetEnabledAsync(bot, true).ConfigureAwait(false),
			"OFF" => await SetEnabledAsync(bot, false).ConfigureAwait(false),
			"PLAN" when hasAppId => await ShowPlanAsync(bot, appId).ConfigureAwait(false),
			"REPLAN" when hasAppId => await ReplanAsync(bot, appId).ConfigureAwait(false),
			_ => Usage
		};

		return bot.Commands.FormatBotResponse(response);
	}

	private static async Task<string> ReplanAsync(Bot bot, uint appId) {
		BotRunner? runner = DriftRuntime.GetRunner(bot);

		if ((runner == null) || !await runner.ReplanAsync(appId).ConfigureAwait(false)) {
			return "El bot no está conectado.";
		}

		return $"Plan de {appId} eliminado; se regenerará cuando el juego vuelva a estar activo.";
	}

	private static async Task<string> SetEnabledAsync(Bot bot, bool enabled) {
		DriftConfig config = await DriftRuntime.Configs.GetAsync(bot.BotName).ConfigureAwait(false);
		await DriftRuntime.Configs.SaveAsync(bot.BotName, config with { Enabled = enabled }).ConfigureAwait(false);

		return enabled ? "AchievementDrift activado." : "AchievementDrift desactivado.";
	}

	private static string FormatPlanOrMissing(BotState state, uint appId) => state.Plans.TryGetValue(appId, out GamePlan? plan) ? StatusBuilder.FormatPlan(plan) : $"No hay plan para {appId} todavía.";

	private static async Task<string> ShowPlanAsync(Bot bot, uint appId) {
		BotRunner? runner = DriftRuntime.GetRunner(bot);

		if (runner != null) {
			return await runner.WithStateAsync(state => FormatPlanOrMissing(state, appId)).ConfigureAwait(false);
		}

		BotState stored = await DriftRuntime.LoadStateAsync(bot.BotName).ConfigureAwait(false);

		return FormatPlanOrMissing(stored, appId);
	}
}
