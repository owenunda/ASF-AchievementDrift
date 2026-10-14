namespace AchievementDrift.Core;

// GlobalPercent = double.NaN cuando Steam no informa el porcentaje
internal sealed record AchievementInfo(string ApiName, string DisplayName, double GlobalPercent);
