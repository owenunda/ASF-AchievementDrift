"use strict";

const PASSWORD_KEY = "achievementdrift.ipcPassword";
const BOT_KEY = "achievementdrift.bot";

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

async function api(path, options = {}) {
	const headers = { "Content-Type": "application/json" };
	const password = getPassword();

	if (password) {
		headers.Authentication = password;
	}

	const response = await fetch(`../Api/Drift/${path}`, { ...options, headers });

	if (response.status === 401 || response.status === 403) {
		const entered = prompt("Contraseña IPC de ASF (IPCPassword):");

		if (entered !== null) {
			setPassword(entered);

			return api(path, options);
		}

		throw new Error("No autorizado.");
	}

	const body = await response.json();

	if (!body.Success) {
		throw new Error(body.Message || `Error ${response.status}`);
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

const STATE_LABELS = { Active: "▶️ Activo", Paused: "⏸️ Pausado", Completed: "🏁 Completado" };

async function loadStatus() {
	const status = await api(encodeURIComponent(currentBot()));

	document.getElementById("summary").textContent =
		`${status.Enabled ? "Activado" : "Desactivado"}${status.DryRun ? " · DryRun (simulación)" : ""} · ` +
		`${status.Connected ? "conectado" : "desconectado"} · horario ${status.ActiveHours} (${status.TimeZone}) · ` +
		`${status.InActiveWindow ? "dentro" : "fuera"} del horario · máx. ${status.MaxActiveGames} juegos a la vez`;

	const games = document.getElementById("games");

	if (status.Games.length === 0) {
		games.innerHTML = "<p>Todavía no hay planes. Se crean cuando el bot juega un juego elegible dentro del horario.</p>";

		return;
	}

	games.innerHTML = status.Games.map((game) => {
		const next = game.Next
			? `<div class="meta">Siguiente: ${escapeHtml(game.Next.Name)} (${game.Next.GlobalPercent.toFixed(1)}%)` +
				(game.Next.EtaUtc ? ` · ~${new Date(game.Next.EtaUtc).toLocaleString()}` : "") + "</div>"
			: "";

		return `<article class="card">
			<h3>${escapeHtml(game.Name)}</h3>
			<div class="meta">${STATE_LABELS[game.State] || game.State} · ${game.Done}/${game.Total} logros${game.Skipped ? ` · ${game.Skipped} saltados` : ""}</div>
			<div class="bar"><span style="width:${game.ProgressPercent}%"></span></div>
			<div class="meta">${game.ProgressPercent}% del plan · ${game.PlayedHours} h de ${game.PlanHours} h · día ${game.DaysSinceStart}</div>
			${next}
		</article>`;
	}).join("");
}

function parseIds(text) {
	return text.split(",").map((value) => value.trim()).filter(Boolean).map(Number).filter((value) => Number.isInteger(value) && value > 0);
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
		Blacklist: parseIds(form.elements.Blacklist.value),
		Allowlist: parseIds(form.elements.Allowlist.value)
	};
}

async function saveConfig(config) {
	await api(`${encodeURIComponent(currentBot())}/Config`, { method: "PUT", body: JSON.stringify(config) });
	showMessage("Configuración guardada.");
	await loadConfig();
}

function suggestTimeZone(configured) {
	const box = document.getElementById("tz-suggestion");
	const detected = Intl.DateTimeFormat().resolvedOptions().timeZone;

	if (!detected || detected === configured) {
		box.hidden = true;

		return;
	}

	box.innerHTML = `Tu navegador está en <b>${escapeHtml(detected)}</b>. <button type="button">Usar esta zona horaria</button>`;
	box.hidden = false;
	box.querySelector("button").addEventListener("click", () => saveConfig({ ...readForm(), TimeZone: detected }).catch((error) => showMessage(error.message)));
}

function selectTab(name) {
	for (const button of document.querySelectorAll("nav button")) {
		button.classList.toggle("active", button.dataset.tab === name);
	}

	for (const section of document.querySelectorAll("main section")) {
		section.hidden = section.id !== `tab-${name}`;
	}
}

async function refresh() {
	try {
		showMessage("");
		await Promise.all([loadStatus(), loadConfig()]);
	} catch (error) {
		showMessage(error.message);
	}
}

async function init() {
	for (const button of document.querySelectorAll("nav button")) {
		button.addEventListener("click", () => selectTab(button.dataset.tab));
	}

	document.getElementById("config-form").addEventListener("submit", (event) => {
		event.preventDefault();
		saveConfig(readForm()).catch((error) => showMessage(error.message));
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
	setInterval(() => loadStatus().catch((error) => showMessage(error.message)), 60000);
}

init();
