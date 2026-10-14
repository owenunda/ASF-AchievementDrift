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

			if (ASF.WebBrowser == null) {
				return null;
			}

			StoreInfo? info = await WebApis.GetStoreInfoAsync(ASF.WebBrowser, appId).ConfigureAwait(false);

			if (info != null) {
				Entries[appId] = info;
				await JsonFileStore.SaveAsync(FileName, Entries).ConfigureAwait(false);
			}

			return info;
		} finally {
			Lock.Release();
		}
	}
}
