using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
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

	// La vista de logros consulta a Steam en vivo; se cachea un minuto para no repetir la consulta en cada apertura
	private static readonly ConcurrentDictionary<(string BotName, uint AppId, bool Spanish), (DateTime FetchedAtUtc, IReadOnlyList<LiveAchievement> Achievements)> LiveCache = new();
	private static readonly TimeSpan LiveCacheLifetime = TimeSpan.FromMinutes(1);

	// Serializa arranque/parada por bot: un logon y una desconexión solapados no pueden dejar un runner huérfano
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> LifecycleLocks = new(StringComparer.Ordinal);

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

	internal static async Task<GameAchievements> GetGameAchievementsAsync(Bot bot, uint appId) {
		ArgumentNullException.ThrowIfNull(bot);

		DriftConfig config = await Configs.GetAsync(bot.BotName).ConfigureAwait(false);
		ActiveWindow window = ActiveWindow.FromConfig(config);
		IReadOnlyList<LiveAchievement>? live = await GetLiveAchievementsAsync(bot, appId).ConfigureAwait(false);
		Dictionary<string, double> percents = await Percents.GetAsync(appId).ConfigureAwait(false) ?? [];
		StoreInfo? store = await Store.GetAsync(appId).ConfigureAwait(false);
		string? storeName = store is { Found: true } ? store.Name : null;
		BotRunner? runner = GetRunner(bot);

		if (runner != null) {
			return await runner.WithStateAsync(state => BuildGame(appId, storeName, live, percents, state, window)).ConfigureAwait(false);
		}

		BotState stored = await LoadStateAsync(bot.BotName).ConfigureAwait(false);

		return BuildGame(appId, storeName, live, percents, stored, window);
	}

	internal static async Task<BotState> LoadStateAsync(string botName) => await JsonFileStore.LoadAsync<BotState>(BotRunner.StateFileName(botName)).ConfigureAwait(false) ?? new BotState();

	internal static void RegisterHandler(Bot bot, StatsHandler handler) => Handlers[bot] = handler;

	internal static async Task StartAsync(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		SemaphoreSlim lifecycleLock = GetLifecycleLock(bot);
		await lifecycleLock.WaitAsync().ConfigureAwait(false);

		try {
			await StopRunnerAsync(bot).ConfigureAwait(false);
			Runners[bot] = await BotRunner.StartAsync(bot).ConfigureAwait(false);
		} finally {
			lifecycleLock.Release();
		}
	}

	internal static async Task StopAsync(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		SemaphoreSlim lifecycleLock = GetLifecycleLock(bot);
		await lifecycleLock.WaitAsync().ConfigureAwait(false);

		try {
			await StopRunnerAsync(bot).ConfigureAwait(false);
		} finally {
			lifecycleLock.Release();
		}
	}

	internal static async Task UnregisterAsync(Bot bot) {
		await StopAsync(bot).ConfigureAwait(false);
		Handlers.TryRemove(bot, out _);
	}

	private static GameAchievements BuildGame(uint appId, string? storeName, IReadOnlyList<LiveAchievement>? live, Dictionary<string, double> percents, BotState state, ActiveWindow window) {
		GamePlan? plan = state.Plans.GetValueOrDefault(appId);
		string name = plan?.GameName ?? storeName ?? appId.ToString(CultureInfo.InvariantCulture);

		return GameAchievementsBuilder.Build(appId, name, live, percents, plan, state.ActiveGames.Contains(appId), window, DateTime.UtcNow);
	}

	private static SemaphoreSlim GetLifecycleLock(Bot bot) => LifecycleLocks.GetOrAdd(bot.BotName, static _ => new SemaphoreSlim(1, 1));

	private static async Task<IReadOnlyList<LiveAchievement>?> GetLiveAchievementsAsync(Bot bot, uint appId) {
		// Los nombres y descripciones dependen del idioma de la petición
		(string BotName, uint AppId, bool Spanish) key = (bot.BotName, appId, Texts.IsSpanish());

		if (LiveCache.TryGetValue(key, out (DateTime FetchedAtUtc, IReadOnlyList<LiveAchievement> Achievements) cached) && (DateTime.UtcNow - cached.FetchedAtUtc < LiveCacheLifetime)) {
			return cached.Achievements;
		}

		StatsHandler? handler = GetHandler(bot);

		if (!bot.IsConnectedAndLoggedOn || (handler == null)) {
			return null;
		}

		StatsSnapshot? snapshot = await handler.GetAsync(appId).ConfigureAwait(false);

		if (snapshot == null) {
			return null;
		}

		List<LiveAchievement> achievements = snapshot.Achievements
			.Select(static achievement => new LiveAchievement(achievement.ApiName, achievement.DisplayName, achievement.Description, achievement.Icon, achievement.IconGray, achievement.IsUnlocked, achievement.IsProtected))
			.ToList();

		LiveCache[key] = (DateTime.UtcNow, achievements);

		return achievements;
	}

	private static async Task StopRunnerAsync(Bot bot) {
		if (Runners.TryRemove(bot, out BotRunner? runner)) {
			await runner.DisposeAsync().ConfigureAwait(false);
		}
	}
}
