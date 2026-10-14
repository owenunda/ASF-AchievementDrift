using System;
using System.Collections.Generic;

namespace AchievementDrift.Core;

// Live = true cuando la lista viene de Steam en vivo; false cuando solo hay datos del plan (bot desconectado o Steam no respondió)
public sealed record GameAchievements(uint AppId, string Name, bool Live, int Unlocked, int Total, IReadOnlyList<AchievementView> Achievements);

// Status: ver las constantes Status* de GameAchievementsBuilder. AtUtc = cuándo se desbloqueó (o se simuló) o cuándo se estima que saldrá
public sealed record AchievementView(string ApiName, string Name, string Description, Uri? IconUrl, double? GlobalPercent, bool Unlocked, string Status, DateTime? AtUtc);

// Estado en vivo de un logro según Steam; Icon/IconGray son nombres de archivo del CDN ("" si no hay)
internal sealed record LiveAchievement(string ApiName, string Name, string Description, string Icon, string IconGray, bool IsUnlocked, bool IsProtected);
