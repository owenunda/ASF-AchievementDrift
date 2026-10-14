"use strict";

// Ventana de logros de un juego. Usa api(), escapeHtml(), currentBot() y COVER_URLS de app.js (se cargan después, pero solo se llaman en tiempo de ejecución)

const ACHIEVEMENT_STATUS = {
	UnlockedByDrift: { label: "Desbloqueado por Drift", tone: "ok" },
	Unlocked: { label: "Conseguido", tone: "ok" },
	Simulated: { label: "Simulado (DryRun)", tone: "info" },
	Next: { label: "Siguiente", tone: "accent" },
	Scheduled: { label: "Programado", tone: "muted" },
	Skipped: { label: "Saltado", tone: "warn" },
	Protected: { label: "🔒 Protegido (lo valida el servidor)", tone: "warn" },
	Unplanned: { label: "Sin plan", tone: "muted" }
};

let dialogData = null;
let dialogFilter = "all";

function formatDate(value) {
	return new Date(value).toLocaleString([], { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
}

function statusBadge(achievement) {
	const status = ACHIEVEMENT_STATUS[achievement.Status] || { label: achievement.Status, tone: "muted" };
	let text = status.label;

	if (achievement.AtUtc) {
		// Fecha real para lo ya hecho; "~" para estimaciones
		const done = achievement.Unlocked || achievement.Status === "Simulated";
		text += ` · ${done ? "" : "~"}${formatDate(achievement.AtUtc)}`;
	} else if (achievement.Status === "Next" || achievement.Status === "Scheduled") {
		text += " · en pausa";
	}

	return `<span class="badge ${status.tone}">${escapeHtml(text)}</span>`;
}

function renderAchievements() {
	const list = document.getElementById("dlg-list");
	const query = document.getElementById("dlg-search").value.trim().toLowerCase();

	const visible = dialogData.Achievements.filter((achievement) =>
		(dialogFilter === "all" || (dialogFilter === "unlocked") === achievement.Unlocked) &&
		(!query || achievement.Name.toLowerCase().includes(query) || achievement.Description.toLowerCase().includes(query)));

	if (visible.length === 0) {
		list.innerHTML = "<li class=\"ach-empty\">No hay logros que mostrar.</li>";

		return;
	}

	list.innerHTML = visible.map((achievement) => {
		const percent = achievement.GlobalPercent === null ? null : Math.max(0, Math.min(100, achievement.GlobalPercent));
		const icon = achievement.IconUrl
			? `<img class="ach-icon" src="${escapeHtml(achievement.IconUrl)}" alt="" loading="lazy">`
			: "<span class=\"ach-icon placeholder\" aria-hidden=\"true\"></span>";

		return `<li class="ach${achievement.Unlocked ? " unlocked" : ""}" style="--pct:${percent ?? 0}%">
			<span class="ach-check" aria-label="${achievement.Unlocked ? "Conseguido" : "Pendiente"}">${achievement.Unlocked ? "✓" : ""}</span>
			${icon}
			<div class="ach-text">
				<div class="ach-name">${escapeHtml(achievement.Name)}</div>
				${achievement.Description ? `<div class="ach-desc">${escapeHtml(achievement.Description)}</div>` : ""}
				${statusBadge(achievement)}
			</div>
			<span class="ach-pct">${percent === null ? "—" : `${percent.toFixed(1)}%`}</span>
		</li>`;
	}).join("");
}

function setFilter(filter) {
	dialogFilter = filter;

	for (const chip of document.querySelectorAll(".chips button")) {
		chip.classList.toggle("active", chip.dataset.filter === filter);
	}
}

async function openAchievements(appId) {
	const dialog = document.getElementById("achievements-dialog");
	const cover = document.getElementById("dlg-cover");
	const list = document.getElementById("dlg-list");

	dialogData = null;
	setFilter("all");
	document.getElementById("dlg-search").value = "";

	cover.classList.remove("missing");
	cover.dataset.appid = appId;
	cover.dataset.cover = "0";
	cover.src = COVER_URLS[0](appId);

	document.getElementById("dlg-title").textContent = "Cargando…";
	document.getElementById("dlg-progress-text").textContent = "";
	document.getElementById("dlg-progress-pct").textContent = "";
	document.getElementById("dlg-progress-bar").style.width = "0%";
	document.getElementById("dlg-note").hidden = true;
	list.innerHTML = "<li class=\"ach-empty\">Consultando a Steam…</li>";
	dialog.showModal();

	let data;

	try {
		data = await api(`${encodeURIComponent(currentBot())}/Games/${encodeURIComponent(appId)}`);
	} catch (error) {
		list.innerHTML = `<li class="ach-empty">${escapeHtml(error.message)}</li>`;

		return;
	}

	// El usuario pudo cerrar y abrir otro juego mientras esperábamos
	if (String(data.AppId) !== String(cover.dataset.appid)) {
		return;
	}

	dialogData = data;

	const pct = data.Total === 0 ? 0 : Math.round(data.Unlocked * 100 / data.Total);
	document.getElementById("dlg-title").textContent = data.Name;
	document.getElementById("dlg-progress-text").textContent = `Logros conseguidos: ${data.Unlocked} de ${data.Total}`;
	document.getElementById("dlg-progress-pct").textContent = `(${pct}%)`;
	document.getElementById("dlg-progress-bar").style.width = `${pct}%`;

	const note = document.getElementById("dlg-note");
	note.hidden = data.Live;
	note.textContent = data.Live ? "" : "No se pudo consultar Steam en vivo (¿bot desconectado?): se muestran solo los logros del plan.";

	renderAchievements();
}

function initAchievementsDialog() {
	const dialog = document.getElementById("achievements-dialog");

	document.getElementById("games").addEventListener("click", (event) => {
		const button = event.target.closest(".btn-achievements");

		if (button) {
			openAchievements(button.dataset.appid);
		}
	});

	// Un icono que no carga se sustituye por el cuadro neutro ("error" no burbujea: fase de captura)
	document.getElementById("dlg-list").addEventListener("error", (event) => {
		if (event.target instanceof HTMLImageElement && event.target.classList.contains("ach-icon")) {
			const placeholder = document.createElement("span");
			placeholder.className = "ach-icon placeholder";
			placeholder.setAttribute("aria-hidden", "true");
			event.target.replaceWith(placeholder);
		}
	}, true);

	document.getElementById("dlg-close").addEventListener("click", () => dialog.close());

	// Un clic en el fondo oscuro (fuera del contenido) también cierra
	dialog.addEventListener("click", (event) => {
		if (event.target === dialog) {
			dialog.close();
		}
	});

	for (const chip of document.querySelectorAll(".chips button")) {
		chip.addEventListener("click", () => {
			setFilter(chip.dataset.filter);

			if (dialogData) {
				renderAchievements();
			}
		});
	}

	document.getElementById("dlg-search").addEventListener("input", () => {
		if (dialogData) {
			renderAchievements();
		}
	});
}
