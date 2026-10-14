using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;

namespace AchievementDrift.Core;

internal enum ETextKey {
	CmdUsage,
	CmdBotNotConnected,
	CmdReplanDone,
	CmdEnabled,
	CmdDisabled,
	CmdNoPlan,

	TimeZoneServer,
	ValMinDays,
	ValMaxDays,
	ValActiveHours,
	ValTimeZone,
	ValMaxActiveGames,
	ValListNull,
	ValListZero,

	EligEligible,
	EligUnknown,
	EligBlacklisted,
	EligVac,
	EligOnline,
	EligNoAchievements,

	PlanHeader,
	PlanEntry,
	StatusEnabled,
	StatusDisabled,
	StatusDryRun,
	StatusInside,
	StatusOutside,
	StatusHeader,
	StatusNoPlans,
	StatusGame,
	StatusNext,
	StatusPanel,

	LogLoaded,
	LogStarted,
	LogDryRunUnlock,
	LogGoneOrProtected,
	LogUnlocked,
	LogRejected,
	LogExcluded,
	LogEvalError,
	LogUnlockFailed,
	LogUnlockGaveUp,
	LogNewAchievements,
	LogPlanError,
	LogUnlockError,
	LogNoAchievements,
	LogNoPercents,
	LogNoPercentsFallback,
	LogPlanCreated,
	LogTrimmed,
	LogNoStats,
	LogInvalidSchema,
	LogStoreTransient,
	LogStoreRejected,
	LogInvalidConfig,
	LogReadError,
	LogStatsTimeout,
	LogStoreTimeout,

	ApiBotNotFound,
	ApiAppIdZero
}

// Textos del plugin en inglés (por defecto) y español. El idioma sale de CultureInfo.CurrentUICulture,
// que ASF fija con CurrentCulture de ASF.json y la IPC con la cabecera Accept-Language de cada petición
internal static class Texts {
	internal static readonly FrozenDictionary<ETextKey, string> English = new Dictionary<ETextKey, string> {
		[ETextKey.CmdUsage] = "Usage: !drift status | on | off | plan <appid> | replan <appid>",
		[ETextKey.CmdBotNotConnected] = "The bot is not connected.",
		[ETextKey.CmdReplanDone] = "Plan for {0} removed; it will be regenerated when the game is active again.",
		[ETextKey.CmdEnabled] = "AchievementDrift enabled.",
		[ETextKey.CmdDisabled] = "AchievementDrift disabled.",
		[ETextKey.CmdNoPlan] = "There is no plan for {0} yet.",

		[ETextKey.TimeZoneServer] = "{0} (server)",
		[ETextKey.ValMinDays] = "{0} must be between 1 and {1}.",
		[ETextKey.ValMaxDays] = "{0} must be between {1} and {2}.",
		[ETextKey.ValActiveHours] = "{0} must use the HH:mm-HH:mm format (for example 10:00-01:00).",
		[ETextKey.ValTimeZone] = "{0} '{1}' is not a valid time zone (use an IANA id, for example America/Bogota).",
		[ETextKey.ValMaxActiveGames] = "{0} must be between 1 and {1}.",
		[ETextKey.ValListNull] = "{0} cannot be null (use [] for an empty list).",
		[ETextKey.ValListZero] = "The lists cannot contain appid 0.",

		[ETextKey.EligEligible] = "eligible",
		[ETextKey.EligUnknown] = "no store data (add it to Allowlist if it is safe)",
		[ETextKey.EligBlacklisted] = "in Blacklist",
		[ETextKey.EligVac] = "has VAC",
		[ETextKey.EligOnline] = "is online/multiplayer",
		[ETextKey.EligNoAchievements] = "has no achievements",

		[ETextKey.PlanHeader] = "Plan for {0} ({1}) — {2} h played of {3} h:",
		[ETextKey.PlanEntry] = "{0} {1} ({2:0.#}%) — at {3} h",
		[ETextKey.StatusEnabled] = "enabled",
		[ETextKey.StatusDisabled] = "disabled",
		[ETextKey.StatusDryRun] = " · DryRun (simulation)",
		[ETextKey.StatusInside] = "within",
		[ETextKey.StatusOutside] = "outside",
		[ETextKey.StatusHeader] = "AchievementDrift: {0}{1} · schedule {2} ({3}) · {4} the schedule",
		[ETextKey.StatusNoPlans] = "No plans yet. They are created when the bot plays an eligible game within the schedule.",
		[ETextKey.StatusGame] = "{0} {1}: {2}/{3} ({4:0}% of the plan, day {5})",
		[ETextKey.StatusNext] = " · next: {0} ({1:0.#}%)",
		[ETextKey.StatusPanel] = "Web panel: {0} (append it to your ASF address, with the trailing slash)",

		[ETextKey.LogLoaded] = "{0} {1} loaded. Web panel: {2}",
		[ETextKey.LogStarted] = "[AchievementDrift] Started ({0} plans loaded).",
		[ETextKey.LogDryRunUnlock] = "[AchievementDrift][DryRun] Would unlock \"{0}\" ({1:0.#}%) in {2}",
		[ETextKey.LogGoneOrProtected] = "[AchievementDrift] \"{0}\" in {1} no longer exists or is protected; skipped.",
		[ETextKey.LogUnlocked] = "[AchievementDrift] Achievement unlocked: \"{0}\" ({1:0.#}%) in {2}",
		[ETextKey.LogRejected] = "[AchievementDrift] Steam rejected \"{0}\" in {1}; skipped.",
		[ETextKey.LogExcluded] = "[AchievementDrift] Excluding {0} ({1}): {2}.",
		[ETextKey.LogEvalError] = "[AchievementDrift] Error evaluating game {0}; ignored this cycle: {1}",
		[ETextKey.LogUnlockFailed] = "[AchievementDrift] Could not unlock \"{0}\" in {1} (attempt {2}/{3}); the game waits {4} minutes.",
		[ETextKey.LogUnlockGaveUp] = "[AchievementDrift] \"{0}\" in {1} failed {2} times in a row; skipped.",
		[ETextKey.LogNewAchievements] = "[AchievementDrift] {0} new achievements added to the plan of {1}.",
		[ETextKey.LogPlanError] = "[AchievementDrift] Error creating the plan for {0}; retrying in {1} minutes: {2}",
		[ETextKey.LogUnlockError] = "[AchievementDrift] Error unlocking an achievement of {0}: {1}",
		[ETextKey.LogNoAchievements] = "[AchievementDrift] Could not get the achievements of {0}; retrying in {1} minutes.",
		[ETextKey.LogNoPercents] = "[AchievementDrift] Could not get the global percentages of {0} (attempt {1}/{2}); retrying in {3} minutes.",
		[ETextKey.LogNoPercentsFallback] = "[AchievementDrift] Could not get the global percentages of {0} after {1} attempts; creating the plan assuming 50% for each achievement.",
		[ETextKey.LogPlanCreated] = "[AchievementDrift] Plan created for {0}: {1} pending achievements ({2} protected excluded) over ~{3:0} days of play.",
		[ETextKey.LogTrimmed] = "[AchievementDrift] Your ASF build looks trimmed: missing {0}. The plugin will not work properly. Use the ASF «generic» build (with Docker: run ASF-generic on the mcr.microsoft.com/dotnet/aspnet image; see the README).",
		[ETextKey.LogNoStats] = "[AchievementDrift] Steam returned no stats for {0}: {1}",
		[ETextKey.LogInvalidSchema] = "[AchievementDrift] Invalid schema for {0}",
		[ETextKey.LogStoreTransient] = "[AchievementDrift] Steam could not save the achievement of {0} because of a temporary error: {1}",
		[ETextKey.LogStoreRejected] = "[AchievementDrift] Steam refused to save the achievement of {0}: {1}",
		[ETextKey.LogInvalidConfig] = "[AchievementDrift] Invalid configuration for {0} ({1}); using defaults.",
		[ETextKey.LogReadError] = "[AchievementDrift] Could not read {0}, using defaults: {1}",
		[ETextKey.LogStatsTimeout] = "[AchievementDrift] Steam did not answer the stats request for {0} within {1} s; it will be retried later.",
		[ETextKey.LogStoreTimeout] = "[AchievementDrift] Steam did not confirm the achievement of {0} within {1} s; it will be retried later.",

		[ETextKey.ApiBotNotFound] = "The bot {0} does not exist.",
		[ETextKey.ApiAppIdZero] = "The appid must be greater than 0."
	}.ToFrozenDictionary();

	internal static readonly FrozenDictionary<ETextKey, string> Spanish = new Dictionary<ETextKey, string> {
		[ETextKey.CmdUsage] = "Uso: !drift status | on | off | plan <appid> | replan <appid>",
		[ETextKey.CmdBotNotConnected] = "El bot no está conectado.",
		[ETextKey.CmdReplanDone] = "Plan de {0} eliminado; se regenerará cuando el juego vuelva a estar activo.",
		[ETextKey.CmdEnabled] = "AchievementDrift activado.",
		[ETextKey.CmdDisabled] = "AchievementDrift desactivado.",
		[ETextKey.CmdNoPlan] = "No hay plan para {0} todavía.",

		[ETextKey.TimeZoneServer] = "{0} (servidor)",
		[ETextKey.ValMinDays] = "{0} debe estar entre 1 y {1}.",
		[ETextKey.ValMaxDays] = "{0} debe estar entre {1} y {2}.",
		[ETextKey.ValActiveHours] = "{0} debe tener el formato HH:mm-HH:mm (por ejemplo 10:00-01:00).",
		[ETextKey.ValTimeZone] = "{0} '{1}' no es una zona horaria válida (usa un identificador IANA, por ejemplo America/Bogota).",
		[ETextKey.ValMaxActiveGames] = "{0} debe estar entre 1 y {1}.",
		[ETextKey.ValListNull] = "{0} no puede ser null (usa [] para una lista vacía).",
		[ETextKey.ValListZero] = "Las listas no pueden contener el appid 0.",

		[ETextKey.EligEligible] = "elegible",
		[ETextKey.EligUnknown] = "sin datos de tienda (añádelo a Allowlist si es seguro)",
		[ETextKey.EligBlacklisted] = "en Blacklist",
		[ETextKey.EligVac] = "tiene VAC",
		[ETextKey.EligOnline] = "es online/multijugador",
		[ETextKey.EligNoAchievements] = "no tiene logros",

		[ETextKey.PlanHeader] = "Plan de {0} ({1}) — {2} h jugadas de {3} h:",
		[ETextKey.PlanEntry] = "{0} {1} ({2:0.#}%) — a las {3} h",
		[ETextKey.StatusEnabled] = "activado",
		[ETextKey.StatusDisabled] = "desactivado",
		[ETextKey.StatusDryRun] = " · DryRun (simulación)",
		[ETextKey.StatusInside] = "dentro",
		[ETextKey.StatusOutside] = "fuera",
		[ETextKey.StatusHeader] = "AchievementDrift: {0}{1} · horario {2} ({3}) · {4} del horario",
		[ETextKey.StatusNoPlans] = "Todavía no hay planes. Se crean cuando el bot juega un juego elegible dentro del horario.",
		[ETextKey.StatusGame] = "{0} {1}: {2}/{3} ({4:0}% del plan, día {5})",
		[ETextKey.StatusNext] = " · siguiente: {0} ({1:0.#}%)",
		[ETextKey.StatusPanel] = "Panel web: {0} (añádelo a la dirección de tu ASF, con la barra final)",

		[ETextKey.LogLoaded] = "{0} {1} cargado. Panel web: {2}",
		[ETextKey.LogStarted] = "[AchievementDrift] Iniciado ({0} planes cargados).",
		[ETextKey.LogDryRunUnlock] = "[AchievementDrift][DryRun] Desbloquearía \"{0}\" ({1:0.#}%) en {2}",
		[ETextKey.LogGoneOrProtected] = "[AchievementDrift] \"{0}\" en {1} ya no existe o está protegido; se salta.",
		[ETextKey.LogUnlocked] = "[AchievementDrift] Logro desbloqueado: \"{0}\" ({1:0.#}%) en {2}",
		[ETextKey.LogRejected] = "[AchievementDrift] Steam rechazó \"{0}\" en {1}; se salta.",
		[ETextKey.LogExcluded] = "[AchievementDrift] Se excluye {0} ({1}): {2}.",
		[ETextKey.LogEvalError] = "[AchievementDrift] Error al evaluar el juego {0}; se ignora en este ciclo: {1}",
		[ETextKey.LogUnlockFailed] = "[AchievementDrift] No se pudo desbloquear \"{0}\" en {1} (intento {2}/{3}); el juego queda en espera {4} minutos.",
		[ETextKey.LogUnlockGaveUp] = "[AchievementDrift] \"{0}\" en {1} falló {2} veces seguidas; se salta.",
		[ETextKey.LogNewAchievements] = "[AchievementDrift] {0} logros nuevos añadidos al plan de {1}.",
		[ETextKey.LogPlanError] = "[AchievementDrift] Error al crear el plan de {0}; se reintentará en {1} minutos: {2}",
		[ETextKey.LogUnlockError] = "[AchievementDrift] Error al desbloquear un logro de {0}: {1}",
		[ETextKey.LogNoAchievements] = "[AchievementDrift] No se pudieron obtener los logros de {0}; se reintentará en {1} minutos.",
		[ETextKey.LogNoPercents] = "[AchievementDrift] No se pudieron obtener los porcentajes globales de {0} (intento {1}/{2}); se reintentará en {3} minutos.",
		[ETextKey.LogNoPercentsFallback] = "[AchievementDrift] No se pudieron obtener los porcentajes globales de {0} tras {1} intentos; se crea el plan suponiendo un 50 % para cada logro.",
		[ETextKey.LogPlanCreated] = "[AchievementDrift] Plan creado para {0}: {1} logros pendientes ({2} protegidos excluidos) en ~{3:0} días de juego.",
		[ETextKey.LogTrimmed] = "[AchievementDrift] Tu build de ASF parece recortada (trimmed): faltan {0}. El plugin no funcionará bien. Usa la build «generic» de ASF (con Docker: ejecuta ASF-generic sobre la imagen mcr.microsoft.com/dotnet/aspnet; mira el README).",
		[ETextKey.LogNoStats] = "[AchievementDrift] Steam no devolvió estadísticas de {0}: {1}",
		[ETextKey.LogInvalidSchema] = "[AchievementDrift] Esquema inválido para {0}",
		[ETextKey.LogStoreTransient] = "[AchievementDrift] Steam no pudo guardar el logro de {0} por un error temporal: {1}",
		[ETextKey.LogStoreRejected] = "[AchievementDrift] Steam rechazó guardar el logro de {0}: {1}",
		[ETextKey.LogInvalidConfig] = "[AchievementDrift] Configuración inválida para {0} ({1}); se usan valores por defecto.",
		[ETextKey.LogReadError] = "[AchievementDrift] No se pudo leer {0}, se usarán valores por defecto: {1}",
		[ETextKey.LogStatsTimeout] = "[AchievementDrift] Steam no respondió a la consulta de estadísticas de {0} en {1} s; se reintentará más tarde.",
		[ETextKey.LogStoreTimeout] = "[AchievementDrift] Steam no confirmó el logro de {0} en {1} s; se reintentará más tarde.",

		[ETextKey.ApiBotNotFound] = "El bot {0} no existe.",
		[ETextKey.ApiAppIdZero] = "El appid debe ser mayor que 0."
	}.ToFrozenDictionary();

	// Idioma del log: el de ASF, fijado al cargar el plugin. Los mensajes del log no deben seguir el idioma de una petición del panel
	internal static CultureInfo LogCulture { get; set; } = CultureInfo.InvariantCulture;

	internal static string Format(ETextKey key, params object?[] args) => FormatFor(null, key, args);

	internal static string Log(ETextKey key, params object?[] args) => FormatFor(LogCulture, key, args);

	internal static string FormatFor(CultureInfo? culture, ETextKey key, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Get(key, culture), args);

	internal static string Get(ETextKey key, CultureInfo? culture = null) => (IsSpanish(culture) ? Spanish : English)[key];

	internal static bool IsSpanish(CultureInfo? culture = null) => string.Equals((culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName, "es", StringComparison.OrdinalIgnoreCase);
}
