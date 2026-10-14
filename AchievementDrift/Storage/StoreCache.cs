using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AchievementDrift.Core;
using AchievementDrift.Steam;
using ArchiSteamFarm.Core;

namespace AchievementDrift.Storage;

internal sealed class StoreCache : IDisposable {
	private const string FileName = "store-cache.json";

	private static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(30);

	// Fallos de red recientes por appid (solo en memoria): evita reconsultar la tienda en cada tick
	private readonly Dictionary<uint, DateTime> FailedAtUtc = [];
	private readonly SemaphoreSlim Lock = new(1, 1);
	private Dictionary<uint, StoreInfo>? Entries;

	public void Dispose() => Lock.Dispose();

	internal async Task<StoreInfo?> GetAsync(uint appId) {
		await Lock.WaitAsync().ConfigureAwait(false);

		try {
			Entries ??= await JsonFileStore.LoadAsync<Dictionary<uint, StoreInfo>>(FileName).ConfigureAwait(false) ?? [];

			if (Entries.TryGetValue(appId, out StoreInfo? cached)) {
				return cached;
			}

			if (FailedAtUtc.TryGetValue(appId, out DateTime failedAtUtc) && (DateTime.UtcNow - failedAtUtc < FailureLifetime)) {
				return null;
			}

			if (ASF.WebBrowser == null) {
				return null;
			}

			StoreInfo? info = await WebApis.GetStoreInfoAsync(ASF.WebBrowser, appId).ConfigureAwait(false);

			if (info == null) {
				FailedAtUtc[appId] = DateTime.UtcNow;

				return null;
			}

			FailedAtUtc.Remove(appId);
			Entries[appId] = info;
			await JsonFileStore.SaveAsync(FileName, Entries).ConfigureAwait(false);

			return info;
		} finally {
			Lock.Release();
		}
	}
}
