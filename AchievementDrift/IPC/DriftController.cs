using System;
using System.Collections.Generic;
using System.Globalization;
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
		ApplyRequestLanguage();

		Bot? bot = FindBot(botName);

		if (bot == null) {
			return BadRequest(new GenericResponse(false, Texts.Format(ETextKey.ApiBotNotFound, botName)));
		}

		DriftConfig config = await DriftRuntime.Configs.GetAsync(bot.BotName).ConfigureAwait(false);

		return Ok(new GenericResponse<DriftConfig>(config));
	}

	/// <summary>Guarda la configuración de AchievementDrift de un bot (se aplica al instante).</summary>
	[HttpPost("{botName:required}/Config")]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.OK)]
	public async Task<ActionResult<GenericResponse>> ConfigPut(string botName, [FromBody] DriftConfig config) {
		ArgumentNullException.ThrowIfNull(config);
		ApplyRequestLanguage();

		Bot? bot = FindBot(botName);

		if (bot == null) {
			return BadRequest(new GenericResponse(false, Texts.Format(ETextKey.ApiBotNotFound, botName)));
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
		ApplyRequestLanguage();

		if (appId == 0) {
			return BadRequest(new GenericResponse(false, Texts.Get(ETextKey.ApiAppIdZero)));
		}

		Bot? bot = FindBot(botName);

		if (bot == null) {
			return BadRequest(new GenericResponse(false, Texts.Format(ETextKey.ApiBotNotFound, botName)));
		}

		GameAchievements result = await DriftRuntime.GetGameAchievementsAsync(bot, appId).ConfigureAwait(false);

		return Ok(new GenericResponse<GameAchievements>(result));
	}

	/// <summary>Estado de los planes de logros de un bot.</summary>
	[HttpGet("{botName:required}")]
	[ProducesResponseType<GenericResponse<DriftStatus>>((int) HttpStatusCode.OK)]
	public async Task<ActionResult<GenericResponse>> StatusGet(string botName) {
		ApplyRequestLanguage();

		Bot? bot = FindBot(botName);

		if (bot == null) {
			return BadRequest(new GenericResponse(false, Texts.Format(ETextKey.ApiBotNotFound, botName)));
		}

		DriftStatus status = await DriftRuntime.GetStatusAsync(bot).ConfigureAwait(false);

		return Ok(new GenericResponse<DriftStatus>(status));
	}

	// El panel envía su idioma en Accept-Language: los mensajes y nombres de esta petición lo siguen.
	// CurrentUICulture vive en el contexto asíncrono de la petición, así que no afecta a ASF ni a otras peticiones
	private void ApplyRequestLanguage() {
		// Request y Headers.AcceptLanguage no existen en builds recortadas de ASF; HttpContext y el indexador sí
#pragma warning disable ASP0015
		string preferred = HttpContext.Request.Headers["Accept-Language"].ToString().Split(',')[0].Split(';')[0].Trim();
#pragma warning restore ASP0015

		if (preferred.Length == 0) {
			return;
		}

		try {
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(preferred);
		} catch (CultureNotFoundException) {
			// Idioma desconocido: se mantiene el de ASF
		}
	}

	// Resuelve el bot con GetBot (mismo comportamiento que ASF) y devuelve la instancia del registro interno de ASF,
	// de modo que el nombre que llega al sistema de archivos nunca deriva directamente del texto de la petición (CA3003)
	private static Bot? FindBot(string botName) {
		Bot? requested = Bot.GetBot(botName);

		return requested == null ? null : Bot.BotsReadOnly?.Values.FirstOrDefault(candidate => ReferenceEquals(candidate, requested));
	}
}
