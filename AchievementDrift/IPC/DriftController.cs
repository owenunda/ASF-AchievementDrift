using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AchievementDrift.Core;
using AchievementDrift.Runtime;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Steam;
using Microsoft.AspNetCore.Mvc;

namespace AchievementDrift.IPC;

[Route("/Api/Drift")]
public sealed class DriftController : ArchiController {
	/// <summary>Lista los bots configurados.</summary>
	[HttpGet("Bots")]
	[ProducesResponseType<GenericResponse<IReadOnlyList<string>>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse> BotsGet() {
		IReadOnlyList<string> names = Bot.BotsReadOnly?.Keys.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToList() ?? [];

		return Ok(new GenericResponse<IReadOnlyList<string>>(names));
	}

	/// <summary>Lee la configuración de AchievementDrift de un bot.</summary>
	[HttpGet("{botName:required}/Config")]
	[ProducesResponseType<GenericResponse<DriftConfig>>((int) HttpStatusCode.OK)]
	public async Task<ActionResult<GenericResponse>> ConfigGet(string botName) {
		Bot? bot = FindBot(botName);

		if (bot == null) {
			return BadRequest(new GenericResponse(false, $"El bot {botName} no existe."));
		}

		DriftConfig config = await DriftRuntime.Configs.GetAsync(bot.BotName).ConfigureAwait(false);

		return Ok(new GenericResponse<DriftConfig>(config));
	}

	/// <summary>Guarda la configuración de AchievementDrift de un bot (se aplica al instante).</summary>
	[HttpPut("{botName:required}/Config")]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.OK)]
	public async Task<ActionResult<GenericResponse>> ConfigPut(string botName, [FromBody] DriftConfig config) {
		ArgumentNullException.ThrowIfNull(config);

		Bot? bot = FindBot(botName);

		if (bot == null) {
			return BadRequest(new GenericResponse(false, $"El bot {botName} no existe."));
		}

		IReadOnlyList<string> errors = config.Validate();

		if (errors.Count > 0) {
			return BadRequest(new GenericResponse(false, string.Join(" ", errors)));
		}

		await DriftRuntime.Configs.SaveAsync(bot.BotName, config).ConfigureAwait(false);

		return Ok(new GenericResponse(true));
	}

	/// <summary>Logros de un juego: estado en vivo de Steam combinado con los porcentajes globales y el plan.</summary>
	[HttpGet("{botName:required}/Games/{appId:required}")]
	[ProducesResponseType<GenericResponse<GameAchievements>>((int) HttpStatusCode.OK)]
	public async Task<ActionResult<GenericResponse>> GameGet(string botName, uint appId) {
		if (appId == 0) {
			return BadRequest(new GenericResponse(false, "El appid debe ser mayor que 0."));
		}

		Bot? bot = FindBot(botName);

		if (bot == null) {
			return BadRequest(new GenericResponse(false, $"El bot {botName} no existe."));
		}

		GameAchievements result = await DriftRuntime.GetGameAchievementsAsync(bot, appId).ConfigureAwait(false);

		return Ok(new GenericResponse<GameAchievements>(result));
	}

	/// <summary>Estado de los planes de logros de un bot.</summary>
	[HttpGet("{botName:required}")]
	[ProducesResponseType<GenericResponse<DriftStatus>>((int) HttpStatusCode.OK)]
	public async Task<ActionResult<GenericResponse>> StatusGet(string botName) {
		Bot? bot = FindBot(botName);

		if (bot == null) {
			return BadRequest(new GenericResponse(false, $"El bot {botName} no existe."));
		}

		DriftStatus status = await DriftRuntime.GetStatusAsync(bot).ConfigureAwait(false);

		return Ok(new GenericResponse<DriftStatus>(status));
	}

	// Resuelve el bot con GetBot (mismo comportamiento que ASF) y devuelve la instancia del registro interno de ASF,
	// de modo que el nombre que llega al sistema de archivos nunca deriva directamente del texto de la petición (CA3003)
	private static Bot? FindBot(string botName) {
		Bot? requested = Bot.GetBot(botName);

		return requested == null ? null : Bot.BotsReadOnly?.Values.FirstOrDefault(candidate => ReferenceEquals(candidate, requested));
	}
}
