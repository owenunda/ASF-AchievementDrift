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
	private const int MaxPercentFailures = 3;
	private const int MaxUnlockFailures = 5;
	private const int RetryMinutes = 30;

	private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(RetryMinutes);
	private static readonly TimeSpan TickPeriod = TimeSpan.FromMinutes(1);

	private readonly Bot Bot;

	// Juegos con errores repetidos: no ocupan hueco ni desbloquean hasta la hora indicada (spec §7). Solo se usa con StateLock.
	private readonly Dictionary<uint, DateTime> CooldownUntil = [];

	private readonly DriftEngine Engine = new(Random.Shared);
	private readonly ConcurrentDictionary<uint, EEligibility> LoggedEligibility = new();

	// Fallos consecutivos al obtener los porcentajes globales al crear un plan. Solo se usa con StateLock.
	private readonly Dictionary<uint, int> PercentFailures = [];

	// No se desecha a propósito: un tick o una consulta de estado en curso aún puede liberarlo tras DisposeAsync
	[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A tick or status query still in flight may release it after DisposeAsync")]
	private readonly SemaphoreSlim StateLock = new(1, 1);

	private readonly BotState State;

	// Fallos consecutivos al desbloquear, por juego (y logro afectado). Solo se usa con StateLock.
	private readonly Dictionary<uint, (string ApiName, int Count)> UnlockFailures = [];

	private bool Disposed;
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

		bot.ArchiLogger.LogGenericInfo(Texts.Log(ETextKey.LogStarted, state.Plans.Count));

		return runner;
	}

	public async ValueTask DisposeAsync() {
		if (Timer != null) {
			await Timer.DisposeAsync().ConfigureAwait(false);
			Timer = null;
		}

		// Esperamos a que termine un tick en curso y marcamos el runner como desechado: a partir de aquí no guarda nada,
		// así no puede pisar el estado que cargue un runner nuevo. No retenemos el lock para no colgar consultas de estado
		// que todavía tengan una referencia a este runner.
		await StateLock.WaitAsync().ConfigureAwait(false);

		try {
			Disposed = true;
		} finally {
			StateLock.Release();
		}
	}

	internal async Task<bool> ReplanAsync(uint appId) {
		await StateLock.WaitAsync().ConfigureAwait(false);

		try {
			if (Disposed) {
				return false;
			}

			State.Plans.Remove(appId);
			State.ActiveGames.Remove(appId);
			CooldownUntil.Remove(appId);
			PercentFailures.Remove(appId);
			UnlockFailures.Remove(appId);
			await JsonFileStore.SaveAsync(StateFileName(Bot.BotName), State).ConfigureAwait(false);

			return true;
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

	// Devuelve false si hubo un error transitorio (Steam no respondió): el logro sigue pendiente y se reintenta más tarde
	private async Task<bool> ExecuteUnlockAsync(UnlockDecision decision, DriftConfig config, ActiveWindow window, DateTime nowUtc) {
		GamePlan plan = State.Plans[decision.AppId];
		PlannedAchievement entry = decision.Entry;

		if (config.DryRun) {
			DriftEngine.RecordUnlock(plan, entry, EEntryState.Simulated, nowUtc);
			Bot.ArchiLogger.LogGenericInfo(Texts.Log(ETextKey.LogDryRunUnlock, entry.DisplayName, entry.GlobalPercent, plan.GameName));

			return true;
		}

		StatsHandler? handler = DriftRuntime.GetHandler(Bot);
		StatsSnapshot? snapshot = handler == null ? null : await handler.GetAsync(decision.AppId).ConfigureAwait(false);

		if ((handler == null) || (snapshot == null)) {
			return false;
		}

		await SyncPlanAsync(plan, snapshot, config, window).ConfigureAwait(false);

		if (entry.State != EEntryState.Pending) {
			return true;
		}

		SchemaAchievement? target = snapshot.Achievements.FirstOrDefault(achievement => achievement.ApiName == entry.ApiName);

		if ((target == null) || target.IsProtected) {
			DriftEngine.ApplyResult(entry, EEntryState.Skipped, nowUtc);
			Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogGoneOrProtected, entry.DisplayName, plan.GameName));

			return true;
		}

		bool? success = await handler.UnlockAsync(decision.AppId, target, snapshot).ConfigureAwait(false);

		switch (success) {
			case true:
				DriftEngine.RecordUnlock(plan, entry, EEntryState.Unlocked, nowUtc);
				Bot.ArchiLogger.LogGenericInfo(Texts.Log(ETextKey.LogUnlocked, entry.DisplayName, entry.GlobalPercent, plan.GameName));

				return true;
			case false:
				DriftEngine.ApplyResult(entry, EEntryState.Skipped, nowUtc);
				Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogRejected, entry.DisplayName, plan.GameName));

				return true;
			default:
				// Error de red o respuesta transitoria de Steam
				return false;
		}
	}

	private HashSet<uint> GetCoolingDown(DateTime nowUtc) {
		foreach (uint appId in CooldownUntil.Where(pair => pair.Value <= nowUtc).Select(static pair => pair.Key).ToList()) {
			CooldownUntil.Remove(appId);
		}

		return [.. CooldownUntil.Keys];
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
						Bot.ArchiLogger.LogGenericInfo(Texts.Log(ETextKey.LogExcluded, info?.Name ?? appId.ToString(CultureInfo.InvariantCulture), appId, SafetyRules.Describe(eligibility, Texts.LogCulture)));
					}
				}
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogEvalError, appId, e.Message));
			}
		}

		return eligible;
	}

	private void OnTimer(object? state) => _ = TickAsync();

	private void RegisterUnlockFailure(UnlockDecision decision, DateTime nowUtc) {
		PlannedAchievement entry = decision.Entry;
		int count = UnlockFailures.TryGetValue(decision.AppId, out (string ApiName, int Count) previous) && (previous.ApiName == entry.ApiName) ? previous.Count + 1 : 1;
		string gameName = State.Plans.TryGetValue(decision.AppId, out GamePlan? plan) ? plan.GameName : decision.AppId.ToString(CultureInfo.InvariantCulture);

		// El juego deja su hueco a otros mientras tanto; su plan queda en pausa
		StartCooldown(decision.AppId, nowUtc);

		if (count < MaxUnlockFailures) {
			UnlockFailures[decision.AppId] = (entry.ApiName, count);
			Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogUnlockFailed, entry.DisplayName, gameName, count, MaxUnlockFailures, RetryMinutes));

			return;
		}

		UnlockFailures.Remove(decision.AppId);

		if (entry.State == EEntryState.Pending) {
			DriftEngine.ApplyResult(entry, EEntryState.Skipped, nowUtc);
		}

		Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogUnlockGaveUp, entry.DisplayName, gameName, MaxUnlockFailures));
	}

	private void StartCooldown(uint appId, DateTime nowUtc) => CooldownUntil[appId] = nowUtc + RetryDelay;

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
		Bot.ArchiLogger.LogGenericInfo(Texts.Log(ETextKey.LogNewAchievements, newOnes.Count, plan.GameName));
	}

	private async Task TickAsync() {
		// Si el tick anterior (o una consulta de estado) sigue en curso, esperamos un poco antes de rendirnos
		if (!await StateLock.WaitAsync(30_000).ConfigureAwait(false)) {
			return;
		}

		try {
			if (Disposed || !Bot.IsConnectedAndLoggedOn) {
				return;
			}

			DriftConfig config = await DriftRuntime.Configs.GetAsync(Bot.BotName).ConfigureAwait(false);
			ActiveWindow window = ActiveWindow.FromConfig(config);
			DateTime nowUtc = DateTime.UtcNow;
			HashSet<uint> coolingDown = GetCoolingDown(nowUtc);

			IReadOnlyCollection<uint> eligible = config.Enabled ? await GetEligiblePlayingAsync(config).ConfigureAwait(false) : [];
			TickResult result = Engine.Tick(State, nowUtc, eligible, coolingDown, config, window);
			bool changed = result.StateChanged;

			foreach (uint appId in result.GamesNeedingPlan) {
				try {
					changed |= await TryCreatePlanAsync(appId, config, window, nowUtc).ConfigureAwait(false);
				} catch (Exception e) {
					StartCooldown(appId, nowUtc);
					Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogPlanError, appId, RetryMinutes, e.Message));
				}
			}

			if (result.Unlock != null) {
				bool handled;

				try {
					handled = await ExecuteUnlockAsync(result.Unlock, config, window, nowUtc).ConfigureAwait(false);
				} catch (Exception e) {
					handled = false;
					Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogUnlockError, result.Unlock.AppId, e.Message));
				}

				if (handled) {
					UnlockFailures.Remove(result.Unlock.AppId);
				} else {
					RegisterUnlockFailure(result.Unlock, nowUtc);
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
		StatsHandler? handler = DriftRuntime.GetHandler(Bot);
		StatsSnapshot? snapshot = handler == null ? null : await handler.GetAsync(appId).ConfigureAwait(false);

		if (snapshot == null) {
			StartCooldown(appId, nowUtc);
			Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogNoAchievements, appId, RetryMinutes));

			return false;
		}

		Dictionary<string, double>? percents = await DriftRuntime.Percents.GetAsync(appId).ConfigureAwait(false);

		if (percents == null) {
			int failures = PercentFailures.GetValueOrDefault(appId) + 1;

			if (failures < MaxPercentFailures) {
				PercentFailures[appId] = failures;
				StartCooldown(appId, nowUtc);
				Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogNoPercents, appId, failures, MaxPercentFailures, RetryMinutes));

				return false;
			}

			// Spec §3.1: sin porcentaje se usa el 50 % (PlanGenerator.NormalizePercent con NaN)
			Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogNoPercentsFallback, appId, MaxPercentFailures));
			percents = [];
		}

		PercentFailures.Remove(appId);

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
		Bot.ArchiLogger.LogGenericInfo(Texts.Log(ETextKey.LogPlanCreated, gameName, pending.Count, protectedCount, days));

		return true;
	}
}
