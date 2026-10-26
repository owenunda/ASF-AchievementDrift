"use strict";

// Textos del panel en inglés y español. El idioma se elige con los botones EN/ES; la primera vez se usa el del navegador
const LANGUAGE_KEY = "achievementdrift.language";

const I18N = {
	en: {
		backTitle: "Back to the ASF panel",
		bot: "Bot",
		tabStatus: "Status",
		tabConfig: "Configuration",
		tabHelp: "Help",
		cfgGroupRun: "How it runs",
		cfgEnabled: "Enabled",
		cfgEnabledHint: "When off, this bot doesn't plan or unlock anything.",
		cfgMode: "Mode",
		cfgModeSimulation: "Simulation",
		cfgModeSimulationHint: "Doesn't touch Steam: it only logs what it would unlock and shows the simulated progress.",
		cfgModeReal: "Real",
		cfgModeRealHint: "Unlocks achievements on your Steam account. Simulated plans are discarded and created again from your real progress.",
		cfgGroupPace: "Pace",
		cfgDuration: "Time to finish each game",
		cfgBetween: "between",
		cfgAnd: "and",
		cfgDays: "days",
		cfgMinDays: "Minimum days per game",
		cfgMaxDays: "Maximum days per game",
		cfgDurationHint: "Each game gets a random duration in this range, so they don't all finish at once.",
		cfgMaxActiveGames: "Games in progress at once",
		cfgMaxActiveGamesHint: "The rest wait for a free slot. If you lower it, the plans of the games left over are deleted.",
		cfgActiveHours: "Active hours",
		cfgFrom: "from",
		cfgTo: "to",
		cfgActiveFrom: "Start time",
		cfgActiveTo: "End time",
		cfgActiveHoursHint: "Achievements only unlock inside these hours. Same start and end = all day.",
		hoursOnly: "{hours} h",
		hoursAndMinutes: "{hours} h {minutes} min",
		hoursSameDay: "{duration} a day: from {from} to {to}.",
		hoursNextDay: "{duration} a day: from {from} to {to} the next day.",
		hoursAllDay: "All day (24 h).",
		cfgTimeZone: "Time zone",
		cfgTimeZoneHint: "Leave it empty to use the time zone of the computer running ASF.",
		cfgGroupGames: "Which games",
		cfgExcludeVac: "Skip games with VAC anti-cheat",
		cfgExcludeVacHint: "Recommended: VAC games carry the most risk.",
		cfgExcludeOnline: "Skip online and multiplayer games",
		cfgExcludeOnlineHint: "Their stats are often checked by the game's servers.",
		cfgBlacklist: "Excluded games",
		cfgBlacklistHint: "Never touched. AppIDs separated by commas: the number in the game's Steam store address.",
		cfgAllowlist: "Always included games",
		cfgAllowlistHint: "Included even if they're online or lack store data. VAC games stay excluded while that option is on.",
		cfgSave: "Save",
		close: "Close",
		filter: "Filter",
		filterAll: "All",
		filterUnlocked: "Unlocked",
		filterLocked: "Locked",
		search: "Search",

		errBanned: "ASF has temporarily blocked your IP or IPC does not allow remote access (403). Wait and reload.",
		errWrongPassword: "The IPC password is wrong. Check IPCPassword and try again (after 5 failures ASF blocks your IP for a while).",
		errNoPassword: "Not authorized: the ASF IPC password (IPCPassword) is required.",
		errStatus: "Error {status}",
		promptPassword: "ASF IPC password (IPCPassword):",
		invalidAppId: "Invalid appid in {list}: {token}",
		configSaved: "Configuration saved.",
		tzSuggestion: "Your browser is in <b>{zone}</b>.",
		tzUse: "Use this time zone",

		modeReal: "Real: unlocking achievements",
		modeSimulation: "Simulation: not touching Steam",
		modeDisabled: "Disabled",
		summaryConnected: "connected",
		summaryDisconnected: "disconnected",
		summaryLine: "{connected} · schedule {hours} ({zone}) · {inside} · max. {max} games at once",
		summaryInside: "within the schedule",
		summaryOutside: "outside the schedule",
		noPlans: "No plans yet. They are created when the bot plays an eligible game within the schedule.",
		stateActive: "▶️ Active",
		statePaused: "⏸️ Paused",
		cardCounts: "{done}/{total} achievements",
		cardSkipped: " · {skipped} skipped",
		cardProgress: "{pct}% of the plan · {played} h of {planned} h · day {day}",
		cardNext: "Next: {name} ({pct}%)",
		viewAchievements: "View achievements",

		statusUnlockedByDrift: "Unlocked by Drift",
		statusUnlocked: "Unlocked",
		statusSimulated: "Simulated (DryRun)",
		statusNext: "Next",
		statusScheduled: "Scheduled",
		statusSkipped: "Skipped",
		statusProtected: "🔒 Protected (validated by the server)",
		statusUnplanned: "Not planned",
		paused: "paused",
		noAchievements: "No achievements to show.",
		loading: "Loading…",
		querying: "Querying Steam…",
		progress: "Achievements unlocked: {unlocked} of {total}",
		offlineNote: "Could not query Steam live (is the bot offline?): only the plan's achievements are shown.",
		ariaUnlocked: "Unlocked",
		ariaLocked: "Locked"
	},
	es: {
		backTitle: "Volver al panel de ASF",
		bot: "Bot",
		tabStatus: "Estado",
		tabConfig: "Configuración",
		tabHelp: "Ayuda",
		cfgGroupRun: "Funcionamiento",
		cfgEnabled: "Activado",
		cfgEnabledHint: "Si lo apagas, este bot no planifica ni desbloquea nada.",
		cfgMode: "Modo",
		cfgModeSimulation: "Simulación",
		cfgModeSimulationHint: "No toca Steam: solo registra lo que desbloquearía y muestra el avance simulado.",
		cfgModeReal: "Real",
		cfgModeRealHint: "Desbloquea logros en tu cuenta de Steam. Los planes simulados se descartan y se crean de nuevo desde tu progreso real.",
		cfgGroupPace: "Ritmo",
		cfgDuration: "Tiempo para completar cada juego",
		cfgBetween: "entre",
		cfgAnd: "y",
		cfgDays: "días",
		cfgMinDays: "Días mínimos por juego",
		cfgMaxDays: "Días máximos por juego",
		cfgDurationHint: "Cada juego recibe una duración al azar dentro de este rango, para que no terminen todos a la vez.",
		cfgMaxActiveGames: "Juegos en progreso a la vez",
		cfgMaxActiveGamesHint: "Los demás esperan a que se libere un hueco. Si lo bajas, se borra el plan de los juegos que sobren.",
		cfgActiveHours: "Horario activo",
		cfgFrom: "desde",
		cfgTo: "hasta",
		cfgActiveFrom: "Hora de inicio",
		cfgActiveTo: "Hora de fin",
		cfgActiveHoursHint: "Solo se desbloquean logros dentro de este horario. Misma hora de inicio y de fin = todo el día.",
		hoursOnly: "{hours} h",
		hoursAndMinutes: "{hours} h {minutes} min",
		hoursSameDay: "{duration} al día: de {from} a {to}.",
		hoursNextDay: "{duration} al día: de {from} a {to} del día siguiente.",
		hoursAllDay: "Todo el día (24 h).",
		cfgTimeZone: "Zona horaria",
		cfgTimeZoneHint: "Déjala vacía para usar la zona horaria del equipo donde corre ASF.",
		cfgGroupGames: "Qué juegos",
		cfgExcludeVac: "Saltar juegos con anti-cheat VAC",
		cfgExcludeVacHint: "Recomendado: los juegos con VAC son los de más riesgo.",
		cfgExcludeOnline: "Saltar juegos online y multijugador",
		cfgExcludeOnlineHint: "Sus estadísticas suelen comprobarlas los servidores del juego.",
		cfgBlacklist: "Juegos excluidos",
		cfgBlacklistHint: "Nunca se tocan. AppIDs separados por comas: el número que aparece en la dirección del juego en la tienda de Steam.",
		cfgAllowlist: "Juegos incluidos siempre",
		cfgAllowlistHint: "Se incluyen aunque sean online o no tengan datos de tienda. Los de VAC siguen excluidos mientras esa opción esté activada.",
		cfgSave: "Guardar",
		close: "Cerrar",
		filter: "Filtro",
		filterAll: "Todos",
		filterUnlocked: "Conseguidos",
		filterLocked: "Pendientes",
		search: "Buscar",

		errBanned: "ASF ha bloqueado temporalmente tu IP o la IPC no permite acceso remoto (403). Espera y recarga.",
		errWrongPassword: "La contraseña IPC no es correcta. Revisa IPCPassword y vuelve a intentarlo (tras 5 fallos ASF bloquea tu IP durante un rato).",
		errNoPassword: "No autorizado: hace falta la contraseña IPC de ASF (IPCPassword).",
		errStatus: "Error {status}",
		promptPassword: "Contraseña IPC de ASF (IPCPassword):",
		invalidAppId: "Appid inválido en {list}: {token}",
		configSaved: "Configuración guardada.",
		tzSuggestion: "Tu navegador está en <b>{zone}</b>.",
		tzUse: "Usar esta zona horaria",

		modeReal: "Real: desbloqueando logros",
		modeSimulation: "Simulación: sin tocar Steam",
		modeDisabled: "Desactivado",
		summaryConnected: "conectado",
		summaryDisconnected: "desconectado",
		summaryLine: "{connected} · horario {hours} ({zone}) · {inside} · máx. {max} juegos a la vez",
		summaryInside: "dentro del horario",
		summaryOutside: "fuera del horario",
		noPlans: "Todavía no hay planes. Se crean cuando el bot juega un juego elegible dentro del horario.",
		stateActive: "▶️ Activo",
		statePaused: "⏸️ Pausado",
		cardCounts: "{done}/{total} logros",
		cardSkipped: " · {skipped} saltados",
		cardProgress: "{pct}% del plan · {played} h de {planned} h · día {day}",
		cardNext: "Siguiente: {name} ({pct}%)",
		viewAchievements: "Ver logros",

		statusUnlockedByDrift: "Desbloqueado por Drift",
		statusUnlocked: "Conseguido",
		statusSimulated: "Simulado (DryRun)",
		statusNext: "Siguiente",
		statusScheduled: "Programado",
		statusSkipped: "Saltado",
		statusProtected: "🔒 Protegido (lo valida el servidor)",
		statusUnplanned: "Sin plan",
		paused: "en pausa",
		noAchievements: "No hay logros que mostrar.",
		loading: "Cargando…",
		querying: "Consultando a Steam…",
		progress: "Logros conseguidos: {unlocked} de {total}",
		offlineNote: "No se pudo consultar Steam en vivo (¿bot desconectado?): se muestran solo los logros del plan.",
		ariaUnlocked: "Conseguido",
		ariaLocked: "Pendiente"
	}
};

function detectLanguage() {
	try {
		const saved = localStorage.getItem(LANGUAGE_KEY);

		if (saved && I18N[saved]) {
			return saved;
		}
	} catch {
		// Sin almacenamiento: se usa el idioma del navegador
	}

	const preferred = (navigator.languages && navigator.languages[0]) || navigator.language || "en";

	return preferred.toLowerCase().startsWith("es") ? "es" : "en";
}

let LANG = detectLanguage();

// t("clave", { nombre: valor }) → texto del idioma activo con {nombre} sustituido (los valores NO se escapan)
function t(key, params = {}) {
	const template = I18N[LANG][key] ?? I18N.en[key] ?? key;

	return template.replace(/\{(\w+)\}/g, (match, name) => (name in params ? String(params[name]) : match));
}

function locale() {
	return LANG === "es" ? "es" : "en";
}

function applyLanguage() {
	document.documentElement.lang = LANG;

	for (const element of document.querySelectorAll("[data-i18n]")) {
		element.textContent = t(element.dataset.i18n);
	}

	for (const element of document.querySelectorAll("[data-i18n-placeholder]")) {
		element.placeholder = t(element.dataset.i18nPlaceholder);
	}

	for (const element of document.querySelectorAll("[data-i18n-title]")) {
		element.title = t(element.dataset.i18nTitle);
	}

	for (const element of document.querySelectorAll("[data-i18n-aria]")) {
		element.setAttribute("aria-label", t(element.dataset.i18nAria));
	}

	for (const block of document.querySelectorAll("[data-lang]")) {
		block.hidden = block.dataset.lang !== LANG;
	}

	for (const button of document.querySelectorAll("[data-lang-button]")) {
		button.classList.toggle("active", button.dataset.langButton === LANG);
		button.setAttribute("aria-pressed", String(button.dataset.langButton === LANG));
	}
}

// onChange se llama tras cambiar de idioma para volver a pintar lo generado por JavaScript
function initLanguage(onChange) {
	applyLanguage();

	for (const button of document.querySelectorAll("[data-lang-button]")) {
		button.addEventListener("click", () => {
			if (button.dataset.langButton === LANG) {
				return;
			}

			LANG = button.dataset.langButton;

			try {
				localStorage.setItem(LANGUAGE_KEY, LANG);
			} catch {
				// Ignorado
			}

			applyLanguage();
			onChange();
		});
	}
}
