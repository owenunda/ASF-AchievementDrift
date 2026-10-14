"use strict";

const PASSWORD_KEY = "achievementdrift.ipcPassword";
const BOT_KEY = "achievementdrift.bot";
const MAX_APP_ID = 4294967295;

// ASF bloquea la IP tras 5 contraseñas incorrectas: como mucho un aviso de contraseña a la vez,
// un solo reintento por petición y sin sondeo automático mientras no estemos autorizados
let passwordPrompt = null;
let unauthorized = false;

function getPassword() {
	try {
		return localStorage.getItem(PASSWORD_KEY) || "";
	} catch {
		return "";
	}
}

function setPassword(value) {
	try {
		localStorage.setItem(PASSWORD_KEY, value);
	} catch {
		// Sin almacenamiento disponible: se pedirá de nuevo
	}
}

function clearPassword() {
	try {
		localStorage.removeItem(PASSWORD_KEY);
	} catch {
		// Ignorado
	}
}

// Todas las peticiones que reciban 401 a la vez comparten el mismo aviso
function askPassword() {
	if (!passwordPrompt) {
		passwordPrompt = Promise.resolve()
			.then(() => {
				const entered = prompt(t("promptPassword"));

				if (!entered) {
					return false;
				}

				setPassword(entered);

				return true;
			})
			.finally(() => {
				passwordPrompt = null;
			});
	}

	return passwordPrompt;
}

async function api(path, options = {}, isRetry = false) {
	// Accept-Language: la API responde (errores de validación, nombres de logros) en el idioma del panel
	const headers = { "Content-Type": "application/json", "Accept-Language": LANG };
	const password = getPassword();

	if (password) {
		headers.Authentication = password;
	}

	const response = await fetch(`../Api/Drift/${path}`, { ...options, headers });

	if (response.status === 403) {
		// No se pide contraseña: más intentos solo alargarían el bloqueo
		unauthorized = true;

		throw new Error(t("errBanned"));
	}

	if (response.status === 401) {
		if (isRetry) {
			unauthorized = true;
			clearPassword();

			throw new Error(t("errWrongPassword"));
		}

		// Otra petición ya pidió una contraseña nueva mientras esta estaba en curso: reintentamos con ella sin preguntar
		if (getPassword() !== password && getPassword()) {
			return api(path, options, true);
		}

		clearPassword();

		if (!await askPassword()) {
			unauthorized = true;

			throw new Error(t("errNoPassword"));
		}

		return api(path, options, true);
	}

	unauthorized = false;

	let body;

	try {
		body = await response.json();
	} catch {
		throw new Error(t("errStatus", { status: response.status }));
	}

	if (!body.Success) {
		throw new Error(body.Message || t("errStatus", { status: response.status }));
	}

	return body.Result;
}

function showMessage(text) {
	const element = document.getElementById("message");
	element.textContent = text;
	element.hidden = !text;
}

function currentBot() {
	return document.getElementById("bot").value;
}

function escapeHtml(text) {
	const div = document.createElement("div");
	div.textContent = String(text);

	return div.innerHTML;
}

const STATE_LABEL_KEYS = { Active: "stateActive", Paused: "statePaused", Completed: "stateCompleted" };

// Carátula del juego desde el CDN de Steam; si no existe se prueba la ruta antigua y, si tampoco, se muestra un fondo neutro
const COVER_URLS = [
	(appId) => `https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/${appId}/header.jpg`,
	(appId) => `https://cdn.cloudflare.steamstatic.com/steam/apps/${appId}/header.jpg`
];

function coverImage(appId, className) {
	return `<img class="${className}" src="${COVER_URLS[0](appId)}" data-appid="${appId}" data-cover="0" alt="" loading="lazy">`;
}

// Delegado (fase de captura: "error" no burbujea) para todas las carátulas de la página
document.addEventListener("error", (event) => {
	const img = event.target;

	if (!(img instanceof HTMLImageElement) || img.dataset.cover === undefined) {
		return;
	}

	const next = Number(img.dataset.cover) + 1;

	if (next < COVER_URLS.length) {
		img.dataset.cover = String(next);
		img.src = COVER_URLS[next](img.dataset.appid);
	} else {
		img.classList.add("missing");
	}
}, true);

async function loadStatus() {
	const status = await api(encodeURIComponent(currentBot()));

	document.getElementById("summary").textContent =
		t("summaryLine", {
			enabled: t(status.Enabled ? "summaryEnabled" : "summaryDisabled"),
			dryRun: status.DryRun ? t("summaryDryRun") : "",
			connected: t(status.Connected ? "summaryConnected" : "summaryDisconnected"),
			hours: status.ActiveHours,
			zone: status.TimeZone,
			inside: t(status.InActiveWindow ? "summaryInside" : "summaryOutside"),
			max: status.MaxActiveGames
		});

	const games = document.getElementById("games");

	if (status.Games.length === 0) {
		games.innerHTML = `<p>${escapeHtml(t("noPlans"))}</p>`;

		return;
	}

	games.innerHTML = status.Games.map((game) => {
		const next = game.Next
			? `<div class="meta">${t("cardNext", { name: escapeHtml(game.Next.Name), pct: game.Next.GlobalPercent.toFixed(1) })}` +
				(game.Next.EtaUtc ? ` · ~${escapeHtml(new Date(game.Next.EtaUtc).toLocaleString(locale()))}` : "") + "</div>"
			: "";

		return `<article class="card">
			<div class="cover-wrap">${coverImage(game.AppId, "cover")}<span class="cover-fallback">${escapeHtml(game.Name)}</span></div>
			<div class="card-body">
				<h3>${escapeHtml(game.Name)}</h3>
				<div class="meta">${STATE_LABEL_KEYS[game.State] ? t(STATE_LABEL_KEYS[game.State]) : escapeHtml(game.State)} · ${t("cardCounts", { done: game.Done, total: game.Total })}${game.Skipped ? t("cardSkipped", { skipped: game.Skipped }) : ""}</div>
				<div class="bar"><span style="width:${game.ProgressPercent}%"></span></div>
				<div class="meta">${t("cardProgress", { pct: game.ProgressPercent, played: game.PlayedHours, planned: game.PlanHours, day: game.DaysSinceStart })}</div>
				${next}
				<button type="button" class="btn-achievements" data-appid="${game.AppId}">${escapeHtml(t("viewAchievements"))}</button>
			</div>
		</article>`;
	}).join("");
}

// Rechaza (no descarta en silencio) lo que no sea un appid válido: un appid mal escrito en la Blacklist sería un riesgo
function parseIds(text, listName) {
	const ids = [];

	for (const token of text.split(",").map((value) => value.trim()).filter(Boolean)) {
		const value = /^\d+$/.test(token) ? Number(token) : NaN;

		if (!Number.isSafeInteger(value) || value < 1 || value > MAX_APP_ID) {
			throw new Error(t("invalidAppId", { list: listName, token }));
		}

		ids.push(value);
	}

	return ids;
}

async function loadConfig() {
	const config = await api(`${encodeURIComponent(currentBot())}/Config`);
	const form = document.getElementById("config-form");

	for (const key of ["Enabled", "DryRun", "ExcludeVac", "ExcludeOnline"]) {
		form.elements[key].checked = config[key];
	}

	for (const key of ["MinDays", "MaxDays", "ActiveHours", "MaxActiveGames"]) {
		form.elements[key].value = config[key];
	}

	form.elements.TimeZone.value = config.TimeZone || "";
	form.elements.Blacklist.value = (config.Blacklist || []).join(", ");
	form.elements.Allowlist.value = (config.Allowlist || []).join(", ");

	suggestTimeZone(config.TimeZone);
}

function readForm() {
	const form = document.getElementById("config-form");

	return {
		Enabled: form.elements.Enabled.checked,
		DryRun: form.elements.DryRun.checked,
		MinDays: Number(form.elements.MinDays.value),
		MaxDays: Number(form.elements.MaxDays.value),
		ActiveHours: form.elements.ActiveHours.value.trim(),
		TimeZone: form.elements.TimeZone.value.trim() || null,
		MaxActiveGames: Number(form.elements.MaxActiveGames.value),
		ExcludeVac: form.elements.ExcludeVac.checked,
		ExcludeOnline: form.elements.ExcludeOnline.checked,
		Blacklist: parseIds(form.elements.Blacklist.value, "Blacklist"),
		Allowlist: parseIds(form.elements.Allowlist.value, "Allowlist")
	};
}

// readForm puede lanzar (appid inválido): dentro de una función async el error llega al .catch y no se envía nada
async function saveConfig(overrides = {}) {
	const config = { ...readForm(), ...overrides };

	await api(`${encodeURIComponent(currentBot())}/Config`, { method: "PUT", body: JSON.stringify(config) });
	showMessage(t("configSaved"));
	await loadConfig();
}

function suggestTimeZone(configured) {
	const box = document.getElementById("tz-suggestion");
	const detected = Intl.DateTimeFormat().resolvedOptions().timeZone;

	if (!detected || detected === configured) {
		box.hidden = true;

		return;
	}

	box.innerHTML = `${t("tzSuggestion", { zone: escapeHtml(detected) })} <button type="button">${escapeHtml(t("tzUse"))}</button>`;
	box.hidden = false;
	box.querySelector("button").addEventListener("click", () => saveConfig({ TimeZone: detected }).catch((error) => showMessage(error.message)));
}

// Índice de la Ayuda: resalta la sección que se está leyendo
function initHelpToc() {
	for (const doc of document.querySelectorAll(".doc")) {
		const links = [...doc.querySelectorAll(".doc-toc a")];
		const byId = new Map(links.map((link) => [link.getAttribute("href").slice(1), link]));

		const observer = new IntersectionObserver((entries) => {
			const visible = entries.filter((entry) => entry.isIntersecting).sort((first, second) => first.boundingClientRect.top - second.boundingClientRect.top)[0];

			if (visible) {
				for (const link of links) {
					link.classList.toggle("active", link === byId.get(visible.target.id));
				}
			}
		}, { rootMargin: "-10% 0px -70% 0px" });

		for (const section of doc.querySelectorAll(".doc-section")) {
			observer.observe(section);
		}

		links[0]?.classList.add("active");
	}
}

function selectTab(name) {
	for (const button of document.querySelectorAll("nav button")) {
		button.classList.toggle("active", button.dataset.tab === name);
	}

	for (const section of document.querySelectorAll("main > section")) {
		section.hidden = section.id !== `tab-${name}`;
	}
}

async function refresh() {
	try {
		showMessage("");
		// En secuencia: si falta la contraseña, solo la primera petición la pide
		await loadStatus();
		await loadConfig();
	} catch (error) {
		showMessage(error.message);
	}
}

async function init() {
	// Al cambiar de idioma se vuelve a pintar lo generado por JavaScript y se piden de nuevo los textos de la API
	initLanguage(() => {
		refresh();
		refreshAchievementsDialog();
	});

	initAchievementsDialog();
	initHelpToc();

	for (const button of document.querySelectorAll("nav button")) {
		button.addEventListener("click", () => selectTab(button.dataset.tab));
	}

	document.getElementById("config-form").addEventListener("submit", (event) => {
		event.preventDefault();
		saveConfig().catch((error) => showMessage(error.message));
	});

	const select = document.getElementById("bot");
	select.addEventListener("change", () => {
		try {
			localStorage.setItem(BOT_KEY, select.value);
		} catch {
			// Ignorado
		}

		refresh();
	});

	try {
		const bots = await api("Bots");
		select.innerHTML = bots.map((name) => `<option>${escapeHtml(name)}</option>`).join("");

		let remembered = "";

		try {
			remembered = localStorage.getItem(BOT_KEY) || "";
		} catch {
			// Ignorado
		}

		if (bots.includes(remembered)) {
			select.value = remembered;
		}
	} catch (error) {
		showMessage(error.message);

		return;
	}

	await refresh();
	setInterval(() => {
		// En pausa mientras no estemos autorizados; se reanuda tras cualquier petición correcta
		if (!unauthorized) {
			loadStatus().catch((error) => showMessage(error.message));
		}
	}, 60000);
}

init();
