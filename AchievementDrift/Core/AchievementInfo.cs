namespace AchievementDrift.Core;

// GlobalPercent = double.NaN cuando Steam no informa el porcentaje
#pragma warning disable CA1812 // Instanciado por la capa de Steam
internal sealed record AchievementInfo(string ApiName, string DisplayName, double GlobalPercent);
#pragma warning restore CA1812 // Instanciado por la capa de Steam
