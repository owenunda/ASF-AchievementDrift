using System.Collections.Immutable;

namespace AchievementDrift.Core;

// Found = false cuando la tienda no conoce el appid (retirado, región, etc.)
#pragma warning disable CA1812 // Instanciado por la capa de Steam
internal sealed record StoreInfo(bool Found, string Name, ImmutableHashSet<int> Categories);
#pragma warning restore CA1812 // Instanciado por la capa de Steam
