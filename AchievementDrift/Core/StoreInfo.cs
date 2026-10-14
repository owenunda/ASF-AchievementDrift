using System.Collections.Immutable;

namespace AchievementDrift.Core;

// Found = false cuando la tienda no conoce el appid (retirado, región, etc.)
internal sealed record StoreInfo(bool Found, string Name, ImmutableHashSet<int> Categories);
