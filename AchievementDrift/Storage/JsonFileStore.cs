using System;
using System.IO;
using System.Threading.Tasks;
using ArchiSteamFarm;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;

namespace AchievementDrift.Storage;

// Guardamos en config/AchievementDrift: las actualizaciones de plugins mueven la carpeta del plugin (spec §11)
internal static class JsonFileStore {
	internal static readonly string DataDirectory = Path.Combine(SharedInfo.ConfigDirectory, AchievementDriftPlugin.PluginName);

	internal static async Task<T?> LoadAsync<T>(string fileName) where T : class {
		ArgumentException.ThrowIfNullOrEmpty(fileName);

		string path = Path.Combine(DataDirectory, fileName);

		if (!File.Exists(path)) {
			return null;
		}

		try {
			string json = await File.ReadAllTextAsync(path).ConfigureAwait(false);

			return json.ToJsonObject<T>();
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericWarning($"[AchievementDrift] No se pudo leer {path}, se usarán valores por defecto: {e.Message}");

			return null;
		}
	}

	internal static async Task SaveAsync<T>(string fileName, T value) where T : notnull {
		ArgumentException.ThrowIfNullOrEmpty(fileName);

		Directory.CreateDirectory(DataDirectory);

		string path = Path.Combine(DataDirectory, fileName);
		string temporaryPath = $"{path}.tmp";

		await File.WriteAllTextAsync(temporaryPath, value.ToJsonText(true)).ConfigureAwait(false);
		File.Move(temporaryPath, path, true);
	}
}
