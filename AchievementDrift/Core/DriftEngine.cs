using System;
using System.Collections.Generic;
using System.Linq;

namespace AchievementDrift.Core;

internal sealed record UnlockDecision(uint AppId, PlannedAchievement Entry);

internal sealed record TickResult(IReadOnlyList<uint> GamesNeedingPlan, UnlockDecision? Unlock, bool StateChanged) {
	internal static readonly TickResult Idle = new([], null, false);
}

internal sealed class DriftEngine(Random random) {
	private readonly Random Random = random ?? throw new ArgumentNullException(nameof(random));

	internal static void ApplyResult(PlannedAchievement entry, EEntryState state, DateTime nowUtc) {
		ArgumentNullException.ThrowIfNull(entry);

		entry.State = state;
		entry.DoneAtUtc = nowUtc;
	}

	internal static void RecordUnlock(GamePlan plan, PlannedAchievement entry, EEntryState state, DateTime nowUtc) {
		ArgumentNullException.ThrowIfNull(plan);

		ApplyResult(entry, state, nowUtc);

		if (state is EEntryState.Unlocked or EEntryState.Simulated) {
			plan.LastUnlockActiveMinutes = plan.ActiveMinutes;
		}
	}

	internal TickResult Tick(BotState state, DateTime nowUtc, IReadOnlyCollection<uint> eligiblePlaying, IReadOnlySet<uint> coolingDown, DriftConfig config, ActiveWindow window) {
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(eligiblePlaying);
		ArgumentNullException.ThrowIfNull(coolingDown);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(window);

		bool changed = DropDryRunPlans(state, config);

		if (!config.Enabled || !window.Contains(nowUtc)) {
			return changed ? new TickResult([], null, true) : TickResult.Idle;
		}

		// Los juegos en espera por errores repetidos no ocupan hueco: su plan queda en pausa (spec §7)
		HashSet<uint> playing = [.. eligiblePlaying.Where(appId => !coolingDown.Contains(appId))];

		changed |= state.ActiveGames.RemoveAll(appId => !playing.Contains(appId) || (state.Plans.TryGetValue(appId, out GamePlan? plan) && plan.IsCompleted)) > 0;

		if (state.ActiveGames.Count > config.MaxActiveGames) {
			state.ActiveGames.RemoveRange(config.MaxActiveGames, state.ActiveGames.Count - config.MaxActiveGames);
			changed = true;
		}

		changed |= FillSlots(state, playing, config.MaxActiveGames);
		changed |= DropLeftoverPlans(state, playing);

		List<uint> needPlan = [];

		foreach (uint appId in state.ActiveGames) {
			if (state.Plans.TryGetValue(appId, out GamePlan? plan)) {
				plan.ActiveMinutes++;
				changed = true;
			} else {
				needPlan.Add(appId);
			}
		}

		return new TickResult(needPlan, FindDue(state, coolingDown), changed);
	}

	private static bool DropDryRunPlans(BotState state, DriftConfig config) {
		if (config.DryRun) {
			return false;
		}

		// También los planes reales con entradas simuladas durante una pausa en DryRun: se regeneran desde el estado real de Steam (spec §4.5)
		List<uint> simulated = state.Plans.Values.Where(static plan => plan.CreatedInDryRun || plan.Entries.Exists(static entry => entry.State == EEntryState.Simulated)).Select(static plan => plan.AppId).ToList();

		foreach (uint appId in simulated) {
			state.Plans.Remove(appId);
			state.ActiveGames.Remove(appId);
		}

		return simulated.Count > 0;
	}

	// Aunque haya varios logros vencidos (p. ej. tras una pausa), un juego no desbloquea dos veces en menos de MinGapMinutes de su propio juego
	private static bool IsInMinGap(GamePlan plan) => plan.LastUnlockActiveMinutes is long last && (plan.ActiveMinutes - last < PlanGenerator.MinGapMinutes);

	private static UnlockDecision? FindDue(BotState state, IReadOnlySet<uint> coolingDown) => state.ActiveGames
		.Where(appId => !coolingDown.Contains(appId) && state.Plans.ContainsKey(appId))
		.Select(appId => state.Plans[appId])
		.Where(static plan => !IsInMinGap(plan))
		.SelectMany(static plan => plan.Entries.Where(entry => (entry.State == EEntryState.Pending) && (entry.OffsetMinutes <= plan.ActiveMinutes)).Select(entry => new UnlockDecision(plan.AppId, entry)))
		.MaxBy(static decision => -decision.Entry.OffsetMinutes); // MinBy no existe en builds recortadas de ASF

	// Un hueco solo se libera cuando un juego completa su plan: un juego empezado que se sigue jugando pero se quedó sin hueco
	// (p. ej. tras bajar MaxActiveGames) estaría en pausa meses. Se borra su plan; si más adelante consigue hueco, se crea uno
	// nuevo desde el estado real de Steam. Solo cuenta lo que se está jugando ahora: los planes de juegos que ASF dejó de
	// jugar un rato (farmeo de cromos, juegos en espera por errores) se conservan.
	private static bool DropLeftoverPlans(BotState state, HashSet<uint> playing) {
		List<uint> leftover = playing.Where(appId => !state.ActiveGames.Contains(appId) && state.Plans.TryGetValue(appId, out GamePlan? plan) && !plan.IsCompleted).ToList();

		foreach (uint appId in leftover) {
			state.Plans.Remove(appId);
		}

		return leftover.Count > 0;
	}

	private bool FillSlots(BotState state, HashSet<uint> playing, int maxActiveGames) {
		int free = maxActiveGames - state.ActiveGames.Count;

		if (free <= 0) {
			return false;
		}

		List<uint> candidates = playing.Where(appId => !state.ActiveGames.Contains(appId) && !(state.Plans.TryGetValue(appId, out GamePlan? plan) && plan.IsCompleted)).ToList();

		// Primero se retoman juegos empezados, luego juegos nuevos; ambos en orden aleatorio
		IEnumerable<uint> started = candidates.Where(state.Plans.ContainsKey).OrderBy(_ => Random.Next());
		IEnumerable<uint> fresh = candidates.Where(appId => !state.Plans.ContainsKey(appId)).OrderBy(_ => Random.Next());

		List<uint> chosen = started.Concat(fresh).Take(free).ToList();
		state.ActiveGames.AddRange(chosen);

		return chosen.Count > 0;
	}
}
