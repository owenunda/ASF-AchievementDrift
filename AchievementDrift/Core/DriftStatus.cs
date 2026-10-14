using System;
using System.Collections.Generic;

namespace AchievementDrift.Core;

public sealed record DriftStatus(string BotName, bool Connected, bool Enabled, bool DryRun, bool InActiveWindow, string ActiveHours, string TimeZone, int MaxActiveGames, IReadOnlyList<GameStatus> Games);

public sealed record GameStatus(uint AppId, string Name, string State, int Done, int Skipped, int Total, double ProgressPercent, double PlayedHours, double PlanHours, int DaysSinceStart, NextAchievementStatus? Next);

public sealed record NextAchievementStatus(string Name, double GlobalPercent, DateTime? EtaUtc);
