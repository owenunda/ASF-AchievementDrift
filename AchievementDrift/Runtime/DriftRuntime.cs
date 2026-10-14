using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using AchievementDrift.Core;
using AchievementDrift.Steam;
using AchievementDrift.Storage;
using ArchiSteamFarm.Steam;

namespace AchievementDrift.Runtime;

internal static class DriftRuntime {
	internal static readonly ConfigStore Configs = new();
	internal static readonly PercentCache Percents = new();
	internal static readonly StoreCache Store = new();

	private static readonly ConcurrentDictionary<Bot, StatsHandler> Handlers = new();
	private static readonly ConcurrentDictionary<Bot, BotRunner> Runners = new();

	internal static StatsHandler? GetHandler(Bot bot) => Handlers.TryGetValue(bot, out StatsHandler? handler) ? handler : null;

	internal static BotRunner? GetRunner(Bot bot) => Runners.TryGetValue(bot, out BotRunner? runner) ? runner : null;

	internal static async Task<DriftStatus> GetStatusAsync(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		DriftConfig config = await Configs.GetAsync(bot.BotName).ConfigureAwait(false);
		ActiveWindow window = ActiveWindow.FromConfig(config);
		BotRunner? runner = GetRunner(bot);

		if (runner != null) {
			return await runner.WithStateAsync(state => StatusBuilder.Build(bot.BotName, true, config, window, state, DateTime.UtcNow)).ConfigureAwait(false);
		}

		BotState stored = await LoadStateAsync(bot.BotName).ConfigureAwait(false);

		return StatusBuilder.Build(bot.BotName, false, config, window, stored, DateTime.UtcNow);
	}

	internal static async Task<BotState> LoadStateAsync(string botName) => await JsonFileStore.LoadAsync<BotState>(BotRunner.StateFileName(botName)).ConfigureAwait(false) ?? new BotState();

	internal static void RegisterHandler(Bot bot, StatsHandler handler) => Handlers[bot] = handler;

	internal static async Task StartAsync(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		await StopAsync(bot).ConfigureAwait(false);
		Runners[bot] = await BotRunner.StartAsync(bot).ConfigureAwait(false);
	}

	internal static async Task StopAsync(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (Runners.TryRemove(bot, out BotRunner? runner)) {
			await runner.DisposeAsync().ConfigureAwait(false);
		}
	}

	internal static async Task UnregisterAsync(Bot bot) {
		await StopAsync(bot).ConfigureAwait(false);
		Handlers.TryRemove(bot, out _);
	}
}
