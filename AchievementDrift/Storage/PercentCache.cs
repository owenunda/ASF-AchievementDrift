using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using AchievementDrift.Steam;
using ArchiSteamFarm.Core;

namespace AchievementDrift.Storage;

internal sealed class PercentCache {
	private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

	private readonly ConcurrentDictionary<uint, (DateTime FetchedAtUtc, Dictionary<string, double> Percents)> Entries = new();

	internal async Task<Dictionary<string, double>?> GetAsync(uint appId) {
		if (Entries.TryGetValue(appId, out (DateTime FetchedAtUtc, Dictionary<string, double> Percents) cached) && (DateTime.UtcNow - cached.FetchedAtUtc < Lifetime)) {
			return cached.Percents;
		}

		if (ASF.WebBrowser == null) {
			return null;
		}

		Dictionary<string, double>? percents = await WebApis.GetGlobalPercentagesAsync(ASF.WebBrowser, appId).ConfigureAwait(false);

		if (percents != null) {
			Entries[appId] = (DateTime.UtcNow, percents);
		}

		return percents;
	}
}
