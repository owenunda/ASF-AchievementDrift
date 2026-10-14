using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using SteamKit2;
using SteamKit2.Internal;

namespace AchievementDrift.Steam;

internal sealed record StatsSnapshot(IReadOnlyList<SchemaAchievement> Achievements, IReadOnlyDictionary<uint, uint> StatValues, uint CrcStats);

#pragma warning disable CA1812 // Instanciado por el plugin en un paso posterior
internal sealed class StatsHandler(Bot bot) : ClientMsgHandler {
	private readonly Bot Bot = bot ?? throw new ArgumentNullException(nameof(bot));

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

	internal async Task<StatsSnapshot?> GetAsync(uint appId) {
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
		} catch (Exception e) {
			Bot.ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		CMsgClientGetUserStatsResponse response = callback.Response;

		if ((EResult) response.eresult != EResult.OK) {
			Bot.ArchiLogger.LogGenericWarning($"[AchievementDrift] Steam no devolvió estadísticas de {appId}: {(EResult) response.eresult}");

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
				Bot.ArchiLogger.LogGenericWarning($"[AchievementDrift] Esquema inválido para {appId}");

				return null;
			}
		}

		return new StatsSnapshot(SchemaParser.Parse(schema, values), values, response.crc_stats);
	}

	internal async Task<bool?> UnlockAsync(uint appId, SchemaAchievement achievement, StatsSnapshot snapshot) {
		ArgumentNullException.ThrowIfNull(achievement);
		ArgumentNullException.ThrowIfNull(snapshot);

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

		try {
			StoreStatsCallback callback = await new AsyncJob<StoreStatsCallback>(Client, request.SourceJobID).ToLongRunningTask().ConfigureAwait(false);

			return (EResult) callback.Response.eresult == EResult.OK;
		} catch (Exception e) {
			Bot.ArchiLogger.LogGenericWarningException(e);

			return null;
		}
	}

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
#pragma warning restore CA1812 // Instanciado por el plugin en un paso posterior
