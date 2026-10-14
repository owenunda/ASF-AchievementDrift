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

	internal TickResult Tick(BotState state, DateTime nowUtc, IReadOnlyCollection<uint> eligiblePlaying, DriftConfig config, ActiveWindow window) {
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(eligiblePlaying);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(window);

		bool changed = DropDryRunPlans(state, config);

		if (!config.Enabled || !window.Contains(nowUtc)) {
			return changed ? new TickResult([], null, true) : TickResult.Idle;
		}

		HashSet<uint> playing = [.. eligiblePlaying];

		changed |= state.ActiveGames.RemoveAll(appId => !playing.Contains(appId) || (state.Plans.TryGetValue(appId, out GamePlan? plan) && plan.IsCompleted)) > 0;

		if (state.ActiveGames.Count > config.MaxActiveGames) {
			state.ActiveGames.RemoveRange(config.MaxActiveGames, state.ActiveGames.Count - config.MaxActiveGames);
			changed = true;
		}

		changed |= FillSlots(state, playing, config.MaxActiveGames);

		List<uint> needPlan = [];

		foreach (uint appId in state.ActiveGames) {
			if (state.Plans.TryGetValue(appId, out GamePlan? plan)) {
				plan.ActiveMinutes++;
				changed = true;
			} else {
				needPlan.Add(appId);
			}
		}

		return new TickResult(needPlan, FindDue(state), changed);
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

	private static UnlockDecision? FindDue(BotState state) => state.ActiveGames
		.Where(state.Plans.ContainsKey)
		.Select(appId => state.Plans[appId])
		.SelectMany(static plan => plan.Entries.Where(entry => (entry.State == EEntryState.Pending) && (entry.OffsetMinutes <= plan.ActiveMinutes)).Select(entry => new UnlockDecision(plan.AppId, entry)))
		.MinBy(static decision => decision.Entry.OffsetMinutes);

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
