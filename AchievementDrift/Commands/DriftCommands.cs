using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AchievementDrift.Core;
using AchievementDrift.Runtime;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using AsfCommands = ArchiSteamFarm.Steam.Interaction.Commands;

namespace AchievementDrift.Commands;

// !drift status|on|off [bots] y !drift plan|replan [bots] <appid>. Sin bots se usa el bot que recibe el comando;
// con bots (ASF, nombres separados por comas…) responde cada uno en su línea, como los comandos de ASF.
internal static class DriftCommands {
	internal static async Task<string?> HandleAsync(Bot bot, EAccess access, string[] args, ulong steamID = 0) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(args);

		if ((args.Length == 0) || !args[0].Equals("drift", StringComparison.OrdinalIgnoreCase)) {
			return null;
		}

		string subcommand = args.Length > 1 ? args[1].ToUpperInvariant() : "STATUS";
		bool needsAppId = subcommand is "PLAN" or "REPLAN";

		// plan/replan llevan el appid al final; lo que haya entre el subcomando y él son los bots
		uint appId = 0;

		if (needsAppId && ((args.Length < 3) || !uint.TryParse(args[^1], NumberStyles.None, CultureInfo.InvariantCulture, out appId) || (appId == 0))) {
			return access >= EAccess.Master ? bot.Commands.FormatBotResponse(Texts.Get(ETextKey.CmdUsage)) : null;
		}

		int botArgs = args.Length - 2 - (needsAppId ? 1 : 0);

		if (botArgs <= 0) {
			return access >= EAccess.Master ? bot.Commands.FormatBotResponse(await RespondAsync(bot, subcommand, appId, true).ConfigureAwait(false)) : null;
		}

		string botNames = string.Join(",", args.Skip(2).Take(botArgs));
		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? AsfCommands.FormatStaticResponse(Texts.Format(ETextKey.ApiBotNotFound, botNames)) : null;
		}

		IList<string?> responses = await Utilities.InParallel(
			bots.OrderBy(static target => target.BotName, StringComparer.OrdinalIgnoreCase).Select(
				async target => AsfCommands.GetProxyAccess(target, access, steamID) >= EAccess.Master ? target.Commands.FormatBotResponse(await RespondAsync(target, subcommand, appId, false).ConfigureAwait(false)) : null
			)
		).ConfigureAwait(false);

		List<string> valid = responses.OfType<string>().ToList();

		if ((valid.Count > 0) && (subcommand == "STATUS")) {
			valid.Add(StatusBuilder.PanelLine());
		}

		return valid.Count > 0 ? string.Join(Environment.NewLine, valid) : null;
	}

	private static async Task<string> RespondAsync(Bot bot, string subcommand, uint appId, bool withPanel) => subcommand switch {
		"STATUS" => StatusBuilder.FormatText(await DriftRuntime.GetStatusAsync(bot).ConfigureAwait(false), withPanel: withPanel),
		"ON" => await SetEnabledAsync(bot, true).ConfigureAwait(false),
		"OFF" => await SetEnabledAsync(bot, false).ConfigureAwait(false),
		"PLAN" => await ShowPlanAsync(bot, appId).ConfigureAwait(false),
		"REPLAN" => await ReplanAsync(bot, appId).ConfigureAwait(false),
		_ => Texts.Get(ETextKey.CmdUsage)
	};

	private static async Task<string> ReplanAsync(Bot bot, uint appId) {
		BotRunner? runner = DriftRuntime.GetRunner(bot);

		if ((runner == null) || !await runner.ReplanAsync(appId).ConfigureAwait(false)) {
			return Texts.Get(ETextKey.CmdBotNotConnected);
		}

		return Texts.Format(ETextKey.CmdReplanDone, appId);
	}

	private static async Task<string> SetEnabledAsync(Bot bot, bool enabled) {
		DriftConfig config = await DriftRuntime.Configs.GetAsync(bot.BotName).ConfigureAwait(false);
		await DriftRuntime.Configs.SaveAsync(bot.BotName, config with { Enabled = enabled }).ConfigureAwait(false);

		return Texts.Get(enabled ? ETextKey.CmdEnabled : ETextKey.CmdDisabled);
	}

	private static string FormatPlanOrMissing(BotState state, uint appId) => state.Plans.TryGetValue(appId, out GamePlan? plan) ? StatusBuilder.FormatPlan(plan) : Texts.Format(ETextKey.CmdNoPlan, appId);

	private static async Task<string> ShowPlanAsync(Bot bot, uint appId) {
		BotRunner? runner = DriftRuntime.GetRunner(bot);

		if (runner != null) {
			return await runner.WithStateAsync(state => FormatPlanOrMissing(state, appId)).ConfigureAwait(false);
		}

		BotState stored = await DriftRuntime.LoadStateAsync(bot.BotName).ConfigureAwait(false);

		return FormatPlanOrMissing(stored, appId);
	}
}
