# ASF-AchievementDrift — Documento de diseño

- **Fecha:** 2026-10-14
- **Repo:** https://github.com/owenunda/ASF-AchievementDrift
- **Plugin / DLL:** `AchievementDrift`
- **ASF objetivo:** 6.3.10.5 (build `linux-x64` del usuario), `net10.0`
- **Licencia:** Apache-2.0

## 1. Objetivo

Plugin de ArchiSteamFarm que, mientras el bot farmea horas, desbloquea logros de forma
gradual, aleatoria y con apariencia realista.

### Requisitos

1. Los logros con menor % global de obtención tardan más en salir y tienden a salir al final.
2. Cada juego tarda **como mínimo `MinDays` (por defecto 30) días de calendario** en completarse.
3. El orden y los tiempos de desbloqueo son aleatorios.
4. Solo se avanza en juegos que el bot **está jugando en ese momento** (`GamesPlayedWhileIdle` / `!play`).
   Si un juego deja de jugarse, su progreso se pausa.
5. Solo se desbloquea dentro de un **horario humano** configurable, en la zona horaria del usuario.
6. Se excluyen automáticamente los juegos con VAC y los de una lista negra manual.
7. Control por comandos, por endpoints IPC y por una página web propia con documentación.
8. Publicado en GitHub con README y actualizaciones automáticas.

### Fuera de alcance

- Modificar estadísticas numéricas (solo logros).
- Bloquear/resetear logros.
- Elegir qué juegos farmear (eso lo sigue haciendo ASF).

## 2. Arquitectura

Un proyecto `AchievementDrift` basado en `JustArchiNET/ASF-PluginTemplate` (ASF como submódulo git,
central package management) y un proyecto de tests `AchievementDrift.Tests` (xUnit).

| Unidad | Responsabilidad | Depende de |
|---|---|---|
| `AchievementDriftPlugin` | Punto de entrada. `IPlugin`, `IBotCommand2`, `IBotConnection`, `IGitHubPluginUpdates`, `IWebInterface`. Solo cablea las demás piezas. | todas |
| `SteamStatsClient` | Lee esquema + estado de logros (`ClientGetUserStats`) y desbloquea (`ClientStoreUserStats2`) vía SteamKit2. Marca logros protegidos (permisos de servidor). | SteamKit2, `Bot` |
| `GlobalPercentages` | Obtiene el % global de cada logro desde `ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2` (sin API key). Cache en memoria 24 h. | `Bot.ArchiWebHandler.WebBrowser` |
| `GameSafetyFilter` | Decide si un appid es elegible: lista negra manual + detección de VAC vía `store.steampowered.com/api/appdetails` (categoría VAC, id 8). Cache persistente. | `WebBrowser`, `ConfigStore` |
| `PlanGenerator` | **Lógica pura.** Recibe logros (nombre, % global) + configuración + `Random`, devuelve un `Plan`. | nada |
| `ActiveWindow` | **Lógica pura.** Dado un instante UTC, una franja `HH:mm-HH:mm` y una zona IANA, dice si está dentro del horario y cuántos minutos tiene la franja por día. Soporta franjas que cruzan medianoche. | nada |
| `Scheduler` | Timer de 1 minuto. Para cada bot conectado y cada juego en reproducción: suma minutos activos y desbloquea lo que toca. | `ISteamStatsClient`, `IClock`, `PlanStore`, `ConfigStore` |
| `PlanStore` | Persiste planes y progreso en `plugins/AchievementDrift/data/<bot>.json`. | sistema de archivos |
| `ConfigStore` | Persiste configuración en `plugins/AchievementDrift/config/<bot>.json`. Única fuente de configuración. Cambios aplicados en caliente. | sistema de archivos |
| `DriftController` | Endpoints IPC (`ArchiController`). | `PlanStore`, `ConfigStore`, `Scheduler` |
| `www/` | Página web estática (`/drift`): estado, configuración y ayuda. | endpoints IPC |

`SteamStatsClient` y el reloj se usan a través de interfaces (`ISteamStatsClient`, `IClock`) para
poder probar `Scheduler` sin Steam.

## 3. Algoritmo del plan

Se genera la primera vez que un juego elegible aparece en reproducción.

1. **Candidatos:** logros no desbloqueados y no protegidos. Cada uno con su % global `p`
   (acotado a `[0.1, 100]`; si Steam no da %, se usa 50).
2. **Presupuesto:** `T = aleatorio(MinDays, MaxDays) × minutosDeFranjaPorDía × (pendientes / total)`.
   Como en un día de calendario no caben más minutos activos que los de la franja, un juego
   empezado desde cero nunca se completa antes de `MinDays` días.
3. **Orden:** clave `k = U^(1/p)`, `U ~ Uniforme(0,1)`; se ordena por `k` descendente
   (muestreo ponderado Efraimidis-Spirakis). Los comunes tienden a salir antes, los raros al final.
4. **Espaciado:** hueco previo de cada logro `g = (1/√p) × Uniforme(0.5, 1.5)`. Se escalan para que
   `Σg = T` y se aplica un mínimo de 15 minutos activos (si el mínimo no cabe en `T`, `T` se amplía).
   El instante de desbloqueo de cada logro es la suma acumulada de huecos (`offsetMinutes`).

### Mantenimiento del plan

- Logro desbloqueado fuera del plugin → se elimina del plan.
- Steam rechaza el desbloqueo → se marca `Skipped` y se registra en el log.
- El juego añade logros nuevos → se generan con el mismo método y se añaden **después** del último
  logro pendiente, con presupuesto proporcional.
- Cambiar `MinDays`/`MaxDays` no regenera planes existentes; afecta solo a planes nuevos.
  El comando `!drift replan <appid>` regenera uno a mano.

## 4. Scheduler

Cada minuto, por cada bot conectado con `Enabled = true`:

1. Si el instante actual está fuera de la franja (`ActiveWindow`) → no hace nada.
2. Para cada appid que ASF está jugando y es elegible (`GameSafetyFilter`):
   - Si no tiene plan → lo crea (paso 3).
   - `activeMinutes += 1`.
3. Como máximo **un desbloqueo por tick y por bot**: el logro pendiente con menor `offsetMinutes`
   cuyo `offsetMinutes ≤ activeMinutes` de su juego.
4. Si `DryRun = true`, en vez de desbloquear registra `[DryRun] desbloquearía <logro> (<appid>)` y lo
   marca como `Simulated` (no cuenta como obtenido; al pasar a modo real se replanifica).
5. Persiste el estado tras cada cambio.

Al conectar el bot (`IBotConnection`) se recargan sus planes; al desconectar se pausa.

## 5. Configuración

Archivo `plugins/AchievementDrift/config/<bot>.json`, editable desde la web/IPC:

| Clave | Por defecto | Descripción |
|---|---|---|
| `Enabled` | `true` | Activa el plugin para este bot. |
| `DryRun` | `true` | Simula sin desbloquear. Se debe desactivar a mano. |
| `MinDays` | `30` | Días mínimos para completar un juego. |
| `MaxDays` | `45` | Días máximos (techo del sorteo). Debe ser ≥ `MinDays`. |
| `ActiveHours` | `"10:00-01:00"` | Franja horaria; puede cruzar medianoche. |
| `TimeZone` | zona del servidor | Identificador IANA (ej. `America/Bogota`). |
| `ExcludeVac` | `true` | Excluye juegos con VAC. |
| `Blacklist` | `[]` | Appids excluidos siempre. |

Validación: valores inválidos se rechazan en el `PUT` con 400 y mensaje claro; un archivo corrupto en
disco se ignora con un warning y se usan los valores por defecto.

### Zona horaria automática

- Por defecto se usa `TimeZoneInfo.Local` del servidor.
- La página `/drift` detecta la zona del navegador (`Intl.DateTimeFormat().resolvedOptions().timeZone`)
  y, si difiere de la configurada, ofrece aplicarla con un clic.

## 6. Interfaces de usuario

### Comandos (permiso `Master`)

- `!drift status [bot]` — progreso por juego, próximo logro y ETA.
- `!drift on|off [bot]` — cambia `Enabled`.
- `!drift plan <appid> [bot]` — muestra el plan sin desbloquear.
- `!drift replan <appid> [bot]` — regenera el plan de un juego.

### IPC (protegido por `IPCPassword` de ASF, visible en Swagger)

- `GET /Api/Drift/{botNames}` — estado: juegos, logros obtenidos/total, % del plan, días transcurridos,
  próximo logro (nombre, % global, ETA estimada).
- `GET /Api/Drift/{botName}/Config` — configuración.
- `PUT /Api/Drift/{botName}/Config` — guarda configuración (validada).

### Página web `/drift` (`IWebInterface`, archivos estáticos)

- **Estado:** tarjetas por juego con barra de progreso y próximo logro.
- **Configuración:** formulario + aviso de zona horaria detectada.
- **Ayuda:** documentación de uso (opciones, cómo leer el progreso, riesgos, problemas comunes).
- HTML/CSS/JS sin dependencias externas; usa el `IPCPassword` que el usuario introduce (guardado en
  `localStorage`, igual que ASF-ui).

## 7. Errores y seguridad

- Fallos de red con Steam/Web API → reintento en el siguiente tick, sin romper el scheduler.
- Cualquier excepción en un juego se registra y no afecta a otros juegos ni bots.
- Juegos con VAC o en lista negra nunca reciben plan.
- Logros protegidos nunca entran al plan.
- `DryRun = true` por defecto para la primera ejecución.
- README y página de ayuda advierten que modificar logros va contra los términos de Steam.

## 8. Pruebas

Tests xUnit (sin Steam):

- `PlanGenerator`: `Σhuecos = T`; hueco mínimo ≥ 15; juego completo nunca < `MinDays` días con franja
  diaria completa; en 1000 planes con semilla, un logro de 2% sale después de uno de 80% en > 90% de los casos;
  presupuesto proporcional con logros ya obtenidos.
- `ActiveWindow`: franjas normales y que cruzan medianoche; bordes; cambio de horario de verano; zona inválida.
- `Scheduler` (reloj y cliente falsos): pausa sin reproducción o fuera de franja; máx. 1 desbloqueo/tick;
  `Skipped` ante rechazo; elimina logros obtenidos fuera; `DryRun` no llama a desbloquear.
- `ConfigStore`: validación y recuperación de archivo corrupto.

Verificación manual: instalar en el ASF del usuario con `DryRun = true`, revisar `!drift plan` y el log,
luego activar modo real con un juego single-player barato.

## 9. Publicación

- Estructura de `ASF-PluginTemplate` (CI incluido: tag `X.Y.Z.W` → release con zip).
- `IGitHubPluginUpdates` con `RepositoryName = "owenunda/ASF-AchievementDrift"`.
- `README.md` en español: qué hace, cómo funciona el algoritmo, instalación, configuración, comandos,
  IPC, página web, riesgos, FAQ, compilación.
- Descripción del repo: "Plugin para ArchiSteamFarm que desbloquea logros de forma gradual y realista
  mientras farmeas horas…" (ver conversación). Topics: `archisteamfarm`, `asf-plugin`, `steam`,
  `steam-achievements`, `dotnet`, `csharp`.

## 10. Riesgos conocidos

- **Build `linux-x64` recortada:** puede provocar `MissingMethodException`. Mitigación: probar; plan B, build `generic`.
- **APIs internas de ASF** (`ArchiWebHandler`, `CardsFarmer`, mensajes de SteamKit2) pueden cambiar entre versiones.
  Mitigación: submódulo fijado a la versión del usuario; revisar changelogs al actualizar.
- **Cómo obtener los juegos en reproducción:** se verificará en el código fuente de ASF 6.3.10.5 durante la
  implementación cuál es la propiedad fiable (juegos de `GamesPlayedWhileIdle`, `!play` y farmeo de cromos).
- **SDK .NET 10** no instalado en el equipo de desarrollo; se instalará con permiso del usuario.
