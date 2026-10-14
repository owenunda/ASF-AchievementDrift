using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using AchievementDrift.Core;
using ArchiSteamFarm.Core;

namespace AchievementDrift.Storage;

internal sealed class ConfigStore {
	private readonly ConcurrentDictionary<string, DriftConfig> Cache = new(StringComparer.Ordinal);

	internal static string FileName(string botName) => $"{botName}.config.json";

	internal async Task<DriftConfig> GetAsync(string botName) {
		ArgumentException.ThrowIfNullOrEmpty(botName);

		if (Cache.TryGetValue(botName, out DriftConfig? cached)) {
			return cached;
		}

		DriftConfig config = await JsonFileStore.LoadAsync<DriftConfig>(FileName(botName)).ConfigureAwait(false) ?? new DriftConfig();
		IReadOnlyList<string> errors = config.Validate();

		if (errors.Count > 0) {
			ASF.ArchiLogger.LogGenericWarning($"[AchievementDrift] Configuración inválida para {botName} ({string.Join(" ", errors)}); se usan valores por defecto.");
			config = new DriftConfig();
		}

		return Cache.GetOrAdd(botName, config);
	}

	internal async Task SaveAsync(string botName, DriftConfig config) {
		ArgumentException.ThrowIfNullOrEmpty(botName);
		ArgumentNullException.ThrowIfNull(config);

		IReadOnlyList<string> errors = config.Validate();

		if (errors.Count > 0) {
			throw new ArgumentException(string.Join(" ", errors), nameof(config));
		}

		await JsonFileStore.SaveAsync(FileName(botName), config).ConfigureAwait(false);
		Cache[botName] = config;
	}
}
