using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace AchievementDrift.Core;

internal enum EEntryState : byte {
	Pending,
	Unlocked,
	Simulated,
	Skipped
}

internal sealed class PlannedAchievement {
	public string ApiName { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public double GlobalPercent { get; set; }
	public long OffsetMinutes { get; set; }
	public EEntryState State { get; set; }
	public DateTime? DoneAtUtc { get; set; }
}

internal sealed class GamePlan {
	public uint AppId { get; set; }
	public string GameName { get; set; } = "";
	public DateTime CreatedAtUtc { get; set; }
	public bool CreatedInDryRun { get; set; }
	public long ActiveMinutes { get; set; }

	// Minutos activos del plan en el último desbloqueo (real o simulado); null = todavía ninguno
	public long? LastUnlockActiveMinutes { get; set; }
	public List<PlannedAchievement> Entries { get; set; } = [];

	[JsonIgnore]
	public bool IsCompleted => Entries.TrueForAll(static entry => entry.State != EEntryState.Pending);

	[JsonIgnore]
	public long PlanMinutes => Entries.Count == 0 ? 0 : Entries.Max(static entry => entry.OffsetMinutes);

	public PlannedAchievement? GetNextPending() => Entries.Where(static entry => entry.State == EEntryState.Pending).MinBy(static entry => entry.OffsetMinutes);
}

internal sealed class BotState {
	public Dictionary<uint, GamePlan> Plans { get; set; } = [];
	public List<uint> ActiveGames { get; set; } = [];
}
