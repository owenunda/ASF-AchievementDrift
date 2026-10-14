using AchievementDrift.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using SteamKit2;
using SteamKit2.Internal;

namespace AchievementDrift.Steam;

internal sealed record StatsSnapshot(IReadOnlyList<SchemaAchievement> Achievements, IReadOnlyDictionary<uint, uint> StatValues, uint CrcStats);

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "SteamKit2 owns the handler's lifetime and never disposes it; the semaphore holds no OS handle")]
internal sealed class StatsHandler(Bot bot) : ClientMsgHandler {
	// Tiempo que ASF espera una respuesta de Steam (Utilities.ToLongRunningTask)
	private const byte ResponseTimeoutSeconds = 60;

	private readonly Bot Bot = bot ?? throw new ArgumentNullException(nameof(bot));

	// Consultas del mismo juego (y mismo idioma de nombres) en curso: se comparte la respuesta en vez de repetir la petición
	private readonly ConcurrentDictionary<(uint AppId, bool Spanish), Lazy<Task<StatsSnapshot?>>> InFlight = new();

	// Una petición a Steam a la vez por bot: varias "Ver logros" seguidas no llegan a Steam en ráfaga
	private readonly SemaphoreSlim RequestLock = new(1, 1);

	public override void HandleMsg(IPacketMsg packetMsg) {
		ArgumentNullException.ThrowIfNull(packetMsg);

		switch (packetMsg.MsgType) {
			case EMsg.ClientGetUserStatsResponse:
				Client.PostCallback(new GetStatsCallback(packetMsg.TargetJobID, new ClientMsgProtobuf<CMsgClientGetUserStatsResponse>(packetMsg).Body));

				break;
			case EMsg.ClientStoreUserStatsResponse:
				Client.PostCallback(new StoreStatsCallback(packetMsg.TargetJobID, new ClientMsgProtobuf<CMsgClientStoreUserStatsResponse>(packetMsg).Body));

				break;
			default:
				break;
		}
	}

	internal Task<StatsSnapshot?> GetAsync(uint appId) {
		(uint AppId, bool Spanish) key = (appId, Texts.IsSpanish());

		return InFlight.GetOrAdd(key, requestKey => new Lazy<Task<StatsSnapshot?>>(() => FetchAndReleaseAsync(requestKey))).Value;
	}

	private async Task<StatsSnapshot?> FetchAndReleaseAsync((uint AppId, bool Spanish) key) {
		try {
			return await FetchAsync(key.AppId).ConfigureAwait(false);
		} finally {
			InFlight.TryRemove(key, out _);
		}
	}

	private async Task<StatsSnapshot?> FetchAsync(uint appId) {
		await RequestLock.WaitAsync().ConfigureAwait(false);

		try {
			return await RequestStatsAsync(appId).ConfigureAwait(false);
		} finally {
			RequestLock.Release();
		}
	}

	private async Task<StatsSnapshot?> RequestStatsAsync(uint appId) {
		if (!Client.IsConnected) {
			return null;
		}

		ClientMsgProtobuf<CMsgClientGetUserStats> request = new(EMsg.ClientGetUserStats) {
			SourceJobID = Client.GetNextJobID(),
			Body = {
				game_id = appId,
				steam_id_for_user = Bot.SteamID
			}
		};

		Client.Send(request);

		GetStatsCallback callback;

		try {
			callback = await new AsyncJob<GetStatsCallback>(Client, request.SourceJobID).ToLongRunningTask().ConfigureAwait(false);
		} catch (TaskCanceledException) {
			// Steam no respondió a tiempo: es pasajero y quien llama reintenta más tarde
			Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogStatsTimeout, appId, ResponseTimeoutSeconds));

			return null;
		} catch (Exception e) {
			Bot.ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		CMsgClientGetUserStatsResponse response = callback.Response;

		if ((EResult) response.eresult != EResult.OK) {
			Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogNoStats, appId, (EResult) response.eresult));

			return null;
		}

		Dictionary<uint, uint> values = [];

		foreach (CMsgClientGetUserStatsResponse.Stats stat in response.stats) {
			values[stat.stat_id] = stat.stat_value;
		}

		if ((response.schema == null) || (response.schema.Length == 0)) {
			return new StatsSnapshot([], values, response.crc_stats);
		}

		KeyValue schema = new();

		using (MemoryStream stream = new(response.schema)) {
			if (!schema.TryReadAsBinary(stream)) {
				Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogInvalidSchema, appId));

				return null;
			}
		}

		return new StatsSnapshot(SchemaParser.Parse(schema, values), values, response.crc_stats);
	}

	internal async Task<bool?> UnlockAsync(uint appId, SchemaAchievement achievement, StatsSnapshot snapshot) {
		ArgumentNullException.ThrowIfNull(achievement);
		ArgumentNullException.ThrowIfNull(snapshot);

		await RequestLock.WaitAsync().ConfigureAwait(false);

		try {
			return await StoreAsync(appId, achievement, snapshot).ConfigureAwait(false);
		} finally {
			RequestLock.Release();
		}
	}

	private async Task<bool?> StoreAsync(uint appId, SchemaAchievement achievement, StatsSnapshot snapshot) {
		if (!Client.IsConnected) {
			return null;
		}

		ClientMsgProtobuf<CMsgClientStoreUserStats2> request = new(EMsg.ClientStoreUserStats2) {
			SourceJobID = Client.GetNextJobID(),
			Body = {
				game_id = appId,
				settor_steam_id = Bot.SteamID,
				settee_steam_id = Bot.SteamID,
				explicit_reset = false,
				crc_stats = snapshot.CrcStats
			}
		};

		request.Body.stats.Add(new CMsgClientStoreUserStats2.Stats { stat_id = achievement.StatId, stat_value = snapshot.StatValues.GetValueOrDefault(achievement.StatId) | (1u << achievement.Bit) });

		if (achievement.DependencyStatId is uint dependency) {
			request.Body.stats.Add(new CMsgClientStoreUserStats2.Stats { stat_id = dependency, stat_value = Math.Max(snapshot.StatValues.GetValueOrDefault(dependency), achievement.DependencyValue) });
		}

		Client.Send(request);

		StoreStatsCallback callback;

		try {
			callback = await new AsyncJob<StoreStatsCallback>(Client, request.SourceJobID).ToLongRunningTask().ConfigureAwait(false);
		} catch (TaskCanceledException) {
			Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogStoreTimeout, appId, ResponseTimeoutSeconds));

			return null;
		} catch (Exception e) {
			Bot.ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		EResult result = (EResult) callback.Response.eresult;

		if (result == EResult.OK) {
			return true;
		}

		if (IsTransient(result)) {
			// Error temporal de Steam: el logro sigue pendiente y se reintenta más tarde
			Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogStoreTransient, appId, result));

			return null;
		}

		Bot.ArchiLogger.LogGenericWarning(Texts.Log(ETextKey.LogStoreRejected, appId, result));

		return false;
	}

	// Respuestas que indican un problema pasajero de Steam o de la conexión, no un rechazo del logro
	private static bool IsTransient(EResult result) => result is EResult.Busy or EResult.ServiceUnavailable or EResult.Timeout or EResult.RateLimitExceeded or EResult.TryAnotherCM or EResult.NoConnection;

	private sealed class GetStatsCallback : CallbackMsg {
		internal CMsgClientGetUserStatsResponse Response { get; }

		internal GetStatsCallback(JobID jobID, CMsgClientGetUserStatsResponse response) {
			ArgumentNullException.ThrowIfNull(jobID);

			JobID = jobID;
			Response = response ?? throw new ArgumentNullException(nameof(response));
		}
	}

	private sealed class StoreStatsCallback : CallbackMsg {
		internal CMsgClientStoreUserStatsResponse Response { get; }

		internal StoreStatsCallback(JobID jobID, CMsgClientStoreUserStatsResponse response) {
			ArgumentNullException.ThrowIfNull(jobID);

			JobID = jobID;
			Response = response ?? throw new ArgumentNullException(nameof(response));
		}
	}
}
