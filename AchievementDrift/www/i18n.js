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
		cfgEnabled: "Enabled",
		cfgDryRun: "Simulation mode (DryRun): logs only, does not unlock",
		cfgMinDays: "Minimum days per game",
		cfgMaxDays: "Maximum days per game",
		cfgActiveHours: "Active hours (HH:mm-HH:mm)",
		cfgTimeZone: "Time zone (IANA, empty = server)",
		cfgMaxActiveGames: "Games in progress at once",
		cfgExcludeVac: "Exclude VAC games",
		cfgExcludeOnline: "Exclude online/multiplayer games",
		cfgBlacklist: "Blacklist (comma-separated appids)",
		cfgAllowlist: "Allowlist (comma-separated appids)",
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

		summaryEnabled: "Enabled",
		summaryDisabled: "Disabled",
		summaryDryRun: " · DryRun (simulation)",
		summaryConnected: "connected",
		summaryDisconnected: "disconnected",
		summaryLine: "{enabled}{dryRun} · {connected} · schedule {hours} ({zone}) · {inside} · max. {max} games at once",
		summaryInside: "within the schedule",
		summaryOutside: "outside the schedule",
		noPlans: "No plans yet. They are created when the bot plays an eligible game within the schedule.",
		stateActive: "▶️ Active",
		statePaused: "⏸️ Paused",
		stateCompleted: "🏁 Completed",
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
		cfgEnabled: "Activado",
		cfgDryRun: "Modo simulación (DryRun): no desbloquea, solo registra",
		cfgMinDays: "Días mínimos por juego",
		cfgMaxDays: "Días máximos por juego",
		cfgActiveHours: "Horario activo (HH:mm-HH:mm)",
		cfgTimeZone: "Zona horaria (IANA, vacío = servidor)",
		cfgMaxActiveGames: "Juegos en progreso a la vez",
		cfgExcludeVac: "Excluir juegos con VAC",
		cfgExcludeOnline: "Excluir juegos online/multijugador",
		cfgBlacklist: "Blacklist (appids separados por coma)",
		cfgAllowlist: "Allowlist (appids separados por coma)",
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

		summaryEnabled: "Activado",
		summaryDisabled: "Desactivado",
		summaryDryRun: " · DryRun (simulación)",
		summaryConnected: "conectado",
		summaryDisconnected: "desconectado",
		summaryLine: "{enabled}{dryRun} · {connected} · horario {hours} ({zone}) · {inside} · máx. {max} juegos a la vez",
		summaryInside: "dentro del horario",
		summaryOutside: "fuera del horario",
		noPlans: "Todavía no hay planes. Se crean cuando el bot juega un juego elegible dentro del horario.",
		stateActive: "▶️ Activo",
		statePaused: "⏸️ Pausado",
		stateCompleted: "🏁 Completado",
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
