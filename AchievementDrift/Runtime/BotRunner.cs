using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementDrift.Core;
using AchievementDrift.Steam;
using AchievementDrift.Storage;
using ArchiSteamFarm.Steam;

namespace AchievementDrift.Runtime;

internal sealed class BotRunner : IAsyncDisposable {
	private static readonly TimeSpan PlanRetryDelay = TimeSpan.FromMinutes(30);
	private static readonly TimeSpan TickPeriod = TimeSpan.FromMinutes(1);

	private readonly Bot Bot;
	private readonly DriftEngine Engine = new(Random.Shared);
	private readonly ConcurrentDictionary<uint, EEligibility> LoggedEligibility = new();
	private readonly ConcurrentDictionary<uint, DateTime> PlanRetryAfter = new();
	// No se desecha a propósito: un tick o una consulta de estado en curso aún puede liberarlo tras DisposeAsync
	[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A tick or status query still in flight may release it after DisposeAsync")]
	private readonly SemaphoreSlim StateLock = new(1, 1);

	private readonly BotState State;
	private Timer? Timer;

	private BotRunner(Bot bot, BotState state) {
		Bot = bot;
		State = state;
	}

	internal static string StateFileName(string botName) => $"{botName}.state.json";

	internal static async Task<BotRunner> StartAsync(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		BotState state = await DriftRuntime.LoadStateAsync(bot.BotName).ConfigureAwait(false);
		BotRunner runner = new(bot, state);
		runner.Timer = new Timer(runner.OnTimer, null, TickPeriod, TickPeriod);

		bot.ArchiLogger.LogGenericInfo($"[AchievementDrift] Iniciado ({state.Plans.Count} planes cargados).");

		return runner;
	}

	public async ValueTask DisposeAsync() {
		if (Timer != null) {
			await Timer.DisposeAsync().ConfigureAwait(false);
			Timer = null;
		}
	}

	internal async Task ReplanAsync(uint appId) {
		await StateLock.WaitAsync().ConfigureAwait(false);

		try {
			State.Plans.Remove(appId);
			State.ActiveGames.Remove(appId);
			PlanRetryAfter.TryRemove(appId, out _);
			await JsonFileStore.SaveAsync(StateFileName(Bot.BotName), State).ConfigureAwait(false);
		} finally {
			StateLock.Release();
		}
	}

	internal async Task<T> WithStateAsync<T>(Func<BotState, T> func) {
		ArgumentNullException.ThrowIfNull(func);

		await StateLock.WaitAsync().ConfigureAwait(false);

		try {
			return func(State);
		} finally {
			StateLock.Release();
		}
	}

	private static HashSet<uint> GetPlayingAppIds(Bot bot) {
		// !play no es detectable: ASF no guarda qué juegos envía (spec §11)
		if (bot.CardsFarmer.NowFarming) {
			return bot.CardsFarmer.CurrentGamesFarmingReadOnly.Select(static game => game.AppID).ToHashSet();
		}

		return bot.IsPlayingPossible ? bot.BotConfig.GamesPlayedWhileIdle.ToHashSet() : [];
	}

	private async Task ExecuteUnlockAsync(UnlockDecision decision, DriftConfig config, ActiveWindow window, DateTime nowUtc) {
		GamePlan plan = State.Plans[decision.AppId];
		PlannedAchievement entry = decision.Entry;

		if (config.DryRun) {
			DriftEngine.ApplyResult(entry, EEntryState.Simulated, nowUtc);
			Bot.ArchiLogger.LogGenericInfo($"[AchievementDrift][DryRun] Desbloquearía \"{entry.DisplayName}\" ({entry.GlobalPercent.ToString("0.#", CultureInfo.InvariantCulture)}%) en {plan.GameName}");

			return;
		}

		StatsHandler? handler = DriftRuntime.GetHandler(Bot);
		StatsSnapshot? snapshot = handler == null ? null : await handler.GetAsync(decision.AppId).ConfigureAwait(false);

		if ((handler == null) || (snapshot == null)) {
			// Se reintenta en el próximo tick: el logro sigue vencido
			return;
		}

		await SyncPlanAsync(plan, snapshot, config, window).ConfigureAwait(false);

		if (entry.State != EEntryState.Pending) {
			return;
		}

		SchemaAchievement? target = snapshot.Achievements.FirstOrDefault(achievement => achievement.ApiName == entry.ApiName);

		if ((target == null) || target.IsProtected) {
			DriftEngine.ApplyResult(entry, EEntryState.Skipped, nowUtc);
			Bot.ArchiLogger.LogGenericWarning($"[AchievementDrift] \"{entry.DisplayName}\" en {plan.GameName} ya no existe o está protegido; se salta.");

			return;
		}

		bool? success = await handler.UnlockAsync(decision.AppId, target, snapshot).ConfigureAwait(false);

		switch (success) {
			case true:
				DriftEngine.ApplyResult(entry, EEntryState.Unlocked, nowUtc);
				Bot.ArchiLogger.LogGenericInfo($"[AchievementDrift] Logro desbloqueado: \"{entry.DisplayName}\" ({entry.GlobalPercent.ToString("0.#", CultureInfo.InvariantCulture)}%) en {plan.GameName}");

				break;
			case false:
				DriftEngine.ApplyResult(entry, EEntryState.Skipped, nowUtc);
				Bot.ArchiLogger.LogGenericWarning($"[AchievementDrift] Steam rechazó \"{entry.DisplayName}\" en {plan.GameName}; se salta.");

				break;
			default:
				// Error de red: se reintenta en el próximo tick
				break;
		}
	}

	private async Task<IReadOnlyCollection<uint>> GetEligiblePlayingAsync(DriftConfig config) {
		List<uint> eligible = [];

		foreach (uint appId in GetPlayingAppIds(Bot)) {
			// Un fallo en un juego no debe afectar a los demás (spec §7): se trata como no elegible en este tick
			try {
				StoreInfo? info = SafetyRules.NeedsStoreInfo(appId, config) ? await DriftRuntime.Store.GetAsync(appId).ConfigureAwait(false) : null;
				EEligibility eligibility = SafetyRules.Evaluate(appId, config, info);

				if (eligibility == EEligibility.Eligible) {
					eligible.Add(appId);
				}

				if (!LoggedEligibility.TryGetValue(appId, out EEligibility previous) || (previous != eligibility)) {
					LoggedEligibility[appId] = eligibility;

					if (eligibility != EEligibility.Eligible) {
						Bot.ArchiLogger.LogGenericInfo($"[AchievementDrift] Se excluye {info?.Name ?? appId.ToString(CultureInfo.InvariantCulture)} ({appId}): {SafetyRules.Describe(eligibility)}.");
					}
				}
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericWarning($"[AchievementDrift] Error al evaluar el juego {appId}; se ignora en este ciclo: {e.Message}");
			}
		}

		return eligible;
	}

	private void OnTimer(object? state) => _ = TickAsync();

	private async Task SyncPlanAsync(GamePlan plan, StatsSnapshot snapshot, DriftConfig config, ActiveWindow window) {
		Dictionary<string, SchemaAchievement> byName = snapshot.Achievements.DistinctBy(static achievement => achievement.ApiName).ToDictionary(static achievement => achievement.ApiName, StringComparer.Ordinal);

		// Logros desbloqueados fuera del plugin cuentan como hechos
		foreach (PlannedAchievement entry in plan.Entries.Where(static entry => entry.State == EEntryState.Pending)) {
			if (byName.TryGetValue(entry.ApiName, out SchemaAchievement? achievement) && achievement.IsUnlocked) {
				DriftEngine.ApplyResult(entry, EEntryState.Unlocked, DateTime.UtcNow);
			}
		}

		// Logros nuevos (DLC/actualización) se añaden al final del plan
		HashSet<string> planned = plan.Entries.Select(static entry => entry.ApiName).ToHashSet(StringComparer.Ordinal);
		List<SchemaAchievement> added = snapshot.Achievements.Where(achievement => !planned.Contains(achievement.ApiName) && !achievement.IsUnlocked && !achievement.IsProtected).ToList();

		if (added.Count == 0) {
			return;
		}

		Dictionary<string, double> percents = await DriftRuntime.Percents.GetAsync(plan.AppId).ConfigureAwait(false) ?? [];
		List<AchievementInfo> newOnes = added.Select(achievement => new AchievementInfo(achievement.ApiName, achievement.DisplayName, percents.GetValueOrDefault(achievement.ApiName, double.NaN))).ToList();

		PlanGenerator.Append(plan, newOnes, snapshot.Achievements.Count(static achievement => !achievement.IsProtected), config, window.MinutesPerDay, Random.Shared);
		Bot.ArchiLogger.LogGenericInfo($"[AchievementDrift] {newOnes.Count} logros nuevos añadidos al plan de {plan.GameName}.");
	}

	private async Task TickAsync() {
		// Si el tick anterior (o una consulta de estado) sigue en curso, esperamos un poco antes de rendirnos
		if (!await StateLock.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false)) {
			return;
		}

		try {
			if (!Bot.IsConnectedAndLoggedOn) {
				return;
			}

			DriftConfig config = await DriftRuntime.Configs.GetAsync(Bot.BotName).ConfigureAwait(false);
			ActiveWindow window = ActiveWindow.FromConfig(config);
			DateTime nowUtc = DateTime.UtcNow;

			IReadOnlyCollection<uint> eligible = config.Enabled ? await GetEligiblePlayingAsync(config).ConfigureAwait(false) : [];
			TickResult result = Engine.Tick(State, nowUtc, eligible, config, window);
			bool changed = result.StateChanged;

			foreach (uint appId in result.GamesNeedingPlan) {
				try {
					changed |= await TryCreatePlanAsync(appId, config, window, nowUtc).ConfigureAwait(false);
				} catch (Exception e) {
					PlanRetryAfter[appId] = nowUtc + PlanRetryDelay;
					Bot.ArchiLogger.LogGenericWarning($"[AchievementDrift] Error al crear el plan de {appId}; se reintentará en {PlanRetryDelay.TotalMinutes} minutos: {e.Message}");
				}
			}

			if (result.Unlock != null) {
				try {
					await ExecuteUnlockAsync(result.Unlock, config, window, nowUtc).ConfigureAwait(false);
				} catch (Exception e) {
					Bot.ArchiLogger.LogGenericWarning($"[AchievementDrift] Error al desbloquear un logro de {result.Unlock.AppId}; se reintentará en el próximo ciclo: {e.Message}");
				}

				changed = true;
			}

			if (changed) {
				await JsonFileStore.SaveAsync(StateFileName(Bot.BotName), State).ConfigureAwait(false);
			}
		} catch (Exception e) {
			Bot.ArchiLogger.LogGenericException(e);
		} finally {
			StateLock.Release();
		}
	}

	private async Task<bool> TryCreatePlanAsync(uint appId, DriftConfig config, ActiveWindow window, DateTime nowUtc) {
		if (PlanRetryAfter.TryGetValue(appId, out DateTime retryAfter) && (retryAfter > nowUtc)) {
			return false;
		}

		StatsHandler? handler = DriftRuntime.GetHandler(Bot);
		StatsSnapshot? snapshot = handler == null ? null : await handler.GetAsync(appId).ConfigureAwait(false);
		Dictionary<string, double>? percents = snapshot == null ? null : await DriftRuntime.Percents.GetAsync(appId).ConfigureAwait(false);

		if ((snapshot == null) || (percents == null)) {
			PlanRetryAfter[appId] = nowUtc + PlanRetryDelay;
			Bot.ArchiLogger.LogGenericWarning($"[AchievementDrift] No se pudieron obtener los logros de {appId}; se reintentará en {PlanRetryDelay.TotalMinutes} minutos.");

			return false;
		}

		List<AchievementInfo> pending = snapshot.Achievements
			.Where(static achievement => !achievement.IsUnlocked && !achievement.IsProtected)
			.Select(achievement => new AchievementInfo(achievement.ApiName, achievement.DisplayName, percents.GetValueOrDefault(achievement.ApiName, double.NaN)))
			.ToList();

		int total = snapshot.Achievements.Count(static achievement => !achievement.IsProtected);
		StoreInfo? info = await DriftRuntime.Store.GetAsync(appId).ConfigureAwait(false);
		string gameName = info is { Found: true } ? info.Name : appId.ToString(CultureInfo.InvariantCulture);

		GamePlan plan = PlanGenerator.Create(appId, gameName, pending, total, config, window.MinutesPerDay, Random.Shared, nowUtc);
		State.Plans[appId] = plan;

		int protectedCount = snapshot.Achievements.Count(static achievement => achievement.IsProtected);
		double days = window.MinutesPerDay == 0 ? 0 : plan.PlanMinutes / (double) window.MinutesPerDay;
		Bot.ArchiLogger.LogGenericInfo($"[AchievementDrift] Plan creado para {gameName}: {pending.Count} logros pendientes ({protectedCount} protegidos excluidos) en ~{days.ToString("0", CultureInfo.InvariantCulture)} días de juego.");

		return true;
	}
}
