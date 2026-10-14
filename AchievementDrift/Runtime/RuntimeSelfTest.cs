using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using ArchiSteamFarm.Core;

namespace AchievementDrift.Runtime;

// Las builds de ASF por sistema operativo (linux-x64, win-x64…) se publican recortadas (PublishTrimmed): pueden faltar
// miembros de la BCL que ASF no usa pero el plugin sí. Cada comprobación va en su propia lambda para que el
// MissingMethodException del JIT salte dentro del try y no al compilar el método que la llama.
internal static class RuntimeSelfTest {
	private static readonly int[] Sample = [3, 1, 2, 1];
	private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];

	internal static bool Run() {
		List<string> missing = [];

		Check(missing, "Enumerable.MinBy", static () => Sample.MinBy(static value => value));
		Check(missing, "Enumerable.DistinctBy", static () => Sample.DistinctBy(static value => value).Count());
		Check(missing, "TimeOnly.TryParseExact", static () => TimeOnly.TryParseExact("10:00", TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
		Check(missing, "TimeOnly.IsBetween", static () => TimeOnly.FromDateTime(DateTime.UtcNow).IsBetween(new TimeOnly(10, 0), new TimeOnly(1, 0)));
		Check(missing, "TimeZoneInfo.FindSystemTimeZoneById", static () => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneInfo.Utc.Id));
		Check(missing, "TimeZoneInfo.ConvertTimeFromUtc", static () => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.Utc));
		Check(missing, "Math.Clamp", static () => Math.Clamp(5.0, 0.1, 100) + Math.Clamp(5, 0, 10));
		Check(missing, "JsonElement.EnumerateArray/TryGetInt32", static () => SumJsonIds("[{\"id\":22},{\"id\":8}]"));
		Check(missing, "ImmutableHashSet.CreateBuilder", static () => {
			ImmutableHashSet<int>.Builder builder = ImmutableHashSet.CreateBuilder<int>();
			builder.Add(22);

			return builder.ToImmutable().Count;
		});

		if (missing.Count == 0) {
			return true;
		}

		ASF.ArchiLogger.LogGenericError($"[AchievementDrift] Tu build de ASF parece recortada (trimmed): faltan {string.Join(", ", missing)}. El plugin no funcionará bien. Usa la build «generic» de ASF o la imagen oficial de Docker.");

		return false;
	}

	private static void Check<T>(List<string> missing, string name, Func<T> probe) {
		try {
			_ = probe();
		} catch (MissingMethodException) {
			missing.Add(name);
		} catch (TypeLoadException) {
			missing.Add(name);
		} catch (Exception e) {
			missing.Add($"{name} ({e.GetType().Name})");
		}
	}

	private static int SumJsonIds(string json) {
		using JsonDocument document = JsonDocument.Parse(json);

		int sum = 0;

		foreach (JsonElement element in document.RootElement.EnumerateArray()) {
			if (element.TryGetProperty("id", out JsonElement id) && id.TryGetInt32(out int value)) {
				sum += value;
			}
		}

		return sum;
	}
}
