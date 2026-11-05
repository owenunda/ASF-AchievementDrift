using AchievementDrift.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using ArchiSteamFarm.Core;
using SteamKit2;
using SteamKit2.Internal;

namespace AchievementDrift.Runtime;

// Las builds de ASF por sistema operativo (linux-x64, win-x64…) se publican recortadas (PublishTrimmed): les faltan
// miembros e incluso tipos de la BCL que ASF no usa. El plugin evita los que faltan en las builds actuales (MinBy,
// TryGetProperty, HttpPut…) y aquí comprueba que sigan estando los que usa, por si una versión futura de ASF recorta más.
// Cada comprobación va en su propia lambda y devuelve solo tipos básicos: así el MissingMethodException/TypeLoadException
// del JIT salta dentro del try de Check y no al compilar Run, que debe poder ejecutarse siempre.
internal static class RuntimeSelfTest {
	private static readonly int[] Sample = [3, 1, 2, 1];
	private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];

	// null = no se ha comprobado o la build tiene todo lo necesario
	internal static string? Missing { get; private set; }

	internal static bool Supported => Missing == null;

	internal static bool Run() {
		// Sin TimeZoneInfo (todas las builds recortadas) el plugin funciona con la hora local del sistema
		List<string> timeZoneMissing = [];

		Check(timeZoneMissing, "TimeZoneInfo.FindSystemTimeZoneById", static () => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneInfo.Utc.Id).Id);
		Check(timeZoneMissing, "TimeZoneInfo.ConvertTimeFromUtc", static () => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.Local).Ticks);

		if (timeZoneMissing.Count > 0) {
			TimeZones.Available = false;
			ASF.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogTimeZoneLocal));
		}

		List<string> missing = [];

		Check(missing, "Enumerable.MaxBy", static () => Sample.MaxBy(static value => -value));
		Check(missing, "Enumerable.DistinctBy", static () => Sample.DistinctBy(static value => value).Count());
		Check(missing, "TimeOnly.TryParseExact", static () => TimeOnly.TryParseExact("10:00", TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
		Check(missing, "TimeOnly.IsBetween", static () => TimeOnly.FromDateTime(DateTime.UtcNow).IsBetween(new TimeOnly(10, 0), new TimeOnly(1, 0)));
		Check(missing, "DateTime.ToLocalTime", static () => DateTime.UtcNow.ToLocalTime().Ticks);
		Check(missing, "Math.Min/Max", static () => Math.Min(100, Math.Max(0.1, 5.0)) + Math.Clamp(5, 0, 10));
		Check(missing, "JsonElement.EnumerateArray/EnumerateObject/TryGetInt32", static () => SumJsonIds("[{\"id\":22},{\"id\":8}]"));
		Check(missing, "SemaphoreSlim.WaitAsync", static () => new SemaphoreSlim(1, 1).WaitAsync(0).Result);
		Check(missing, "ImmutableHashSet.CreateBuilder", static () => {
			ImmutableHashSet<int>.Builder builder = ImmutableHashSet.CreateBuilder<int>();
			builder.Add(22);

			return builder.ToImmutable().Count;
		});
		Check(missing, "SteamKit2 stats messages", static () => RoundTripStatsMessages());

		if (missing.Count == 0) {
			return true;
		}

		Missing = string.Join(", ", missing);
		ASF.ArchiLogger.LogGenericError(Texts.Log(ETextKey.LogTrimmed, Missing));

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

	// Mismo camino que StatsHandler, sin Steam: construye y serializa las peticiones de logros y vuelve a leer una respuesta.
	// En las builds recortadas de ASF V7 solo sobrevive la serialización con el TypeModel precompilado de SteamKit2
	private static uint RoundTripStatsMessages() {
		ClientMsgProtobuf<CMsgClientGetUserStats> getRequest = new(EMsg.ClientGetUserStats, SteamKitProtobuf.Model) {
			Body = {
				game_id = 440,
				steam_id_for_user = 76561197960287930
			}
		};

		ClientMsgProtobuf<CMsgClientStoreUserStats2> storeRequest = new(EMsg.ClientStoreUserStats2, SteamKitProtobuf.Model) {
			Body = {
				game_id = 440,
				crc_stats = 7
			}
		};

		storeRequest.Body.stats.Add(new CMsgClientStoreUserStats2.Stats { stat_id = 1, stat_value = 2 });

		ClientMsgProtobuf<CMsgClientGetUserStatsResponse> response = new(EMsg.ClientGetUserStatsResponse, SteamKitProtobuf.Model) {
			Body = {
				game_id = 440,
				crc_stats = 7
			}
		};

		response.Body.stats.Add(new CMsgClientGetUserStatsResponse.Stats { stat_id = 1, stat_value = 2 });

		if ((getRequest.Serialize().Length == 0) || (storeRequest.Serialize().Length == 0)) {
			throw new InvalidOperationException(nameof(RoundTripStatsMessages));
		}

		ClientMsgProtobuf<CMsgClientGetUserStatsResponse> parsed = new(new PacketClientMsgProtobuf(EMsg.ClientGetUserStatsResponse, response.Serialize()), SteamKitProtobuf.Model);

		if ((parsed.Body.crc_stats != 7) || (parsed.Body.stats.Count != 1) || (parsed.Body.stats[0].stat_value != 2)) {
			throw new InvalidOperationException(nameof(RoundTripStatsMessages));
		}

		return parsed.Body.crc_stats;
	}

	private static int SumJsonIds(string json) {
		using JsonDocument document = JsonDocument.Parse(json);

		int sum = 0;

		foreach (JsonElement element in document.RootElement.EnumerateArray()) {
			foreach (JsonProperty property in element.EnumerateObject()) {
				if ((property.Name == "id") && property.Value.TryGetInt32(out int value)) {
					sum += value;
				}
			}
		}

		return sum;
	}
}
