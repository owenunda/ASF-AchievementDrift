# Documentación técnica — ASF-AchievementDrift

Guía para quien quiera entender, modificar o publicar el plugin. La documentación de uso está en el [README](../README.md); el diseño original en `docs/superpowers/specs/2026-10-14-achievement-drift-design.md`.

## 1. Arquitectura

Dos proyectos .NET (`net10.0`) basados en `JustArchiNET/ASF-PluginTemplate`, con ASF como submódulo git y central package management:

- `AchievementDrift`: el plugin (una DLL con la web estática en `www/`).
- `AchievementDrift.Tests`: tests MSTest (sin Steam ni ASF en ejecución).

### Unidades

| Unidad | Responsabilidad | Ubicación |
|---|---|---|
| `AchievementDriftPlugin` | Punto de entrada. `IGitHubPluginUpdates`, `IBot`, `IBotCommand2`, `IBotConnection`, `IBotSteamClient`, `IWebInterface`. Solo cablea las demás piezas. | `AchievementDriftPlugin.cs` |
| `StatsHandler` | `ClientMsgHandler` de SteamKit2: lee esquema y estado de logros (`ClientGetUserStats`) y desbloquea (`ClientStoreUserStats2`). | `Steam/StatsHandler.cs` |
| `SchemaParser` | Interpreta el esquema binario (KeyValues): nombre API, nombre visible, bit, protección y dependencias de progreso. | `Steam/SchemaParser.cs` |
| `WebApis` | Porcentajes globales (`GetGlobalAchievementPercentagesForApp/v2`, sin API key) y datos de tienda (`appdetails`). | `Steam/WebApis.cs` |
| `PercentCache` | Cache en memoria (24 h) de los porcentajes globales por appid. | `Storage/PercentCache.cs` |
| `StoreCache` | Cache persistente por appid de los datos de tienda (`store-cache.json`). | `Storage/StoreCache.cs` |
| `ConfigStore` | Configuración por bot (`<bot>.config.json`); única fuente de configuración, con cambios en caliente y validación. | `Storage/ConfigStore.cs` |
| `JsonFileStore` | Lectura/escritura JSON atómica en `config/AchievementDrift/`. | `Storage/JsonFileStore.cs` |
| `SafetyRules` | **Lógica pura.** Decide si un appid es elegible (Blacklist, Allowlist, VAC, online, sin logros, sin datos). | `Core/SafetyRules.cs` |
| `PlanGenerator` | **Lógica pura.** Crea y amplía planes (presupuesto, orden ponderado, espaciado). | `Core/PlanGenerator.cs` |
| `ActiveWindow` | **Lógica pura.** Franja `HH:mm-HH:mm` en una zona IANA, con soporte de franjas que cruzan la medianoche. | `Core/ActiveWindow.cs` |
| `DriftEngine` | **Lógica pura.** Un tick: huecos de juegos activos, minutos activos, siguiente desbloqueo. | `Core/DriftEngine.cs` |
| `StatusBuilder` | Construye el estado (`DriftStatus`) y su formato de texto para `!drift status` y la IPC. | `Core/StatusBuilder.cs` |
| `BotRunner` | Un temporizador de 1 minuto por bot conectado; orquesta lectura de juegos, motor, creación de planes, desbloqueo y guardado. | `Runtime/BotRunner.cs` |
| `DriftRuntime` | Registro estático de runners, handlers y almacenes. | `Runtime/DriftRuntime.cs` |
| `DriftCommands` | Comandos `!drift ...` (permiso `Master`). | `Commands/DriftCommands.cs` |
| `DriftController` | Endpoints IPC (`ArchiController`). | `IPC/DriftController.cs` |
| `www/` | Página `/drift/`: estado, configuración y ayuda (HTML/CSS/JS sin dependencias externas). | `www/` |

El motor es puro y testeable: `DriftEngine.Tick` no toca Steam ni el disco; recibe el estado, la hora, los juegos elegibles en reproducción y la configuración, y devuelve qué hay que crear/desbloquear. Los efectos secundarios viven en `BotRunner`.

### Flujo de un tick (cada minuto, por bot)

1. `BotRunner.TickAsync` toma el candado del estado y comprueba que el bot sigue conectado.
2. **Lectura de juegos:** si `CardsFarmer.NowFarming`, los juegos del farmeo; si no y `IsPlayingPossible`, `GamesPlayedWhileIdle`.
3. **`SafetyRules`:** cada juego se evalúa (consultando `StoreCache` si hace falta) y solo pasan los elegibles. Un fallo en un juego se registra y se trata como no elegible en ese tick.
4. **`DriftEngine.Tick`:** descarta planes creados en DryRun si ya no estamos en DryRun, comprueba `Enabled` y la franja horaria, libera/rellena huecos (`MaxActiveGames`), suma un minuto activo a cada juego activo con plan y devuelve los juegos que necesitan plan y, como máximo, un desbloqueo vencido.
5. **Crear planes:** para cada juego sin plan se lee el esquema (`StatsHandler`) y los porcentajes (`PercentCache`) y se llama a `PlanGenerator.Create`.
6. **Desbloquear:** en DryRun se registra `[AchievementDrift][DryRun] Desbloquearía ...` y la entrada pasa a `Simulated`; en modo real se sincroniza el plan con Steam (logros hechos fuera, logros nuevos), se valida que el logro exista y no sea protegido y se envía `ClientStoreUserStats2`.
7. **Guardar:** si algo cambió, se escribe `<bot>.state.json`.

Todos los mensajes del log llevan el prefijo `[AchievementDrift]` (y `[AchievementDrift][DryRun]` las simulaciones).

## 2. Notas técnicas de ASF 6.3.10.5 (verificadas en el código fuente)

Hallazgos del código de ASF que condicionan el diseño.

### Detección de juegos en reproducción
- `ArchiHandler.PlayGames()` es `internal` y **no guarda** qué juegos envía a Steam. Por eso `!play` no es detectable
  desde un plugin.
- Fuentes públicas fiables:
  - `bot.CardsFarmer.NowFarming` + `bot.CardsFarmer.CurrentGamesFarmingReadOnly` (`Game.AppID`): farmeo de cromos.
  - `bot.BotConfig.GamesPlayedWhileIdle` (`ImmutableList<uint>`): se juegan cuando no se farmean cromos
    (`Bot.ResetGamesPlayed()`), siempre que `bot.IsPlayingPossible` sea `true`.
  - `!pause` también deja sonando `GamesPlayedWhileIdle`; `!play` los sustituye (por eso no se soporta).

### Desbloqueo de logros (SteamKit2)
- Se registra un `ClientMsgHandler` propio con `IBotSteamClient.OnBotSteamHandlersInit` (como ASF-Achievement-Manager).
- Leer: `EMsg.ClientGetUserStats` (`CMsgClientGetUserStats { game_id, steam_id_for_user }`) → respuesta
  `EMsg.ClientGetUserStatsResponse` con `schema` (KeyValues binario), `stats` (`stat_id`, `stat_value`) y `crc_stats`.
- Esquema: en `stats/*` los nodos con `type` = `4` o `ACHIEVEMENTS` tienen `bits/<n>` con `name` (nombre API, el que usa
  la Web API de porcentajes), `display/name/<idioma>` y opcionalmente `permission` (= protegido). Un logro está
  desbloqueado si `stat_value & (1 << bit) != 0`. Si tiene `progress/value/operand1`, depende de un stat INT que hay que
  poner a `progress/max_val`; si ese stat tiene `permission > 1`, el logro también es protegido.
- Escribir: `EMsg.ClientStoreUserStats2` (`CMsgClientStoreUserStats2 { game_id, settor_steam_id, settee_steam_id,
  crc_stats, explicit_reset = false, stats }`) → `EMsg.ClientStoreUserStatsResponse` (`eresult`).
- Esperar respuestas con `new AsyncJob<T>(client, jobId).ToLongRunningTask()` (`ArchiSteamFarm.Core.Utilities`).

### Almacenamiento
- Las actualizaciones de plugins (`Utilities.UpdateFromArchive`) mueven los archivos de la carpeta del plugin a un backup:
  **no se guardan datos en `plugins/AchievementDrift/`**.
- Se usa `config/AchievementDrift/` (`SharedInfo.ConfigDirectory`). ASF solo escanea `config/*.json` del nivel superior
  (bots) y su `FileSystemWatcher` no es recursivo, así que una subcarpeta es segura.

### JSON
- Se usan `JsonUtilities.ToJsonText()` / `ToJsonObject<T>()` de ASF (`JsonSerializerDefaults.Strict`, nombres en
  **PascalCase**, sin naming policy, nullability respetada). La web y la IPC usan PascalCase.
- ASF usa `DefaultJsonTypeInfoResolver` explícito, así que la serialización por reflexión funciona aun en builds recortadas.

### IPC y página web
- Controladores: clases `public` que heredan de `ArchiController`; ASF los descubre solo. Respuestas con `GenericResponse<T>`.
  Los tipos expuestos en sus firmas deben ser `public`.
- Autenticación: cabecera HTTP `Authentication: <IPCPassword>` (`ApiAuthenticationMiddleware`).
- `IWebInterface`: `PhysicalPath` relativo a la carpeta del DLL; `WebPath = "/drift"`. ASF registra `UseDefaultFiles` +
  `UseStaticFiles` para esa ruta. Las rutas no-API que dan 404 se re-ejecutan en `/` (ASF-ui), así que la URL correcta es
  **`/drift/`** con barra final.

### Build
- Target `net10.0`; `Directory.Build.props` de ASF activa `AnalysisMode=AllEnabledByDefault` y
  `TreatWarningsAsErrors` en Release. El código sigue el estilo de ASF (`ConfigureAwait(false)`, cultura invariante).
- Las builds por SO de ASF (como `linux-x64`) se publican con `PublishTrimmed=true`.
- La plantilla oficial ya apunta el submódulo de ASF a `6.3.10.5`.

## 3. Decisiones

- **MSTest en lugar de xUnit.** ASF ya fija la versión de MSTest en su central package management; usar lo mismo evita conflictos de versiones.
- **Configuración solo en el plugin.** Vive en `config/AchievementDrift/<bot>.config.json` y no en el JSON del bot de ASF, para tener una única fuente editable desde la web, la IPC y el disco, con cambios en caliente.
- **`!play` no soportado.** ASF no guarda qué juegos se le pasan (ver §2), así que no hay forma fiable de saber qué se está jugando.
- **Estado `Simulated` y regeneración al salir de DryRun.** En DryRun el desbloqueo no se envía: la entrada pasa a `Simulated` (no cuenta como obtenida). Los planes se marcan con `CreatedInDryRun`; al desactivar `DryRun`, `DriftEngine` los descarta y se regeneran en modo real desde cero.
- **Logros desbloqueados fuera del plugin** se marcan `Unlocked` al sincronizar el plan con Steam (justo antes de cada desbloqueo real). Los logros nuevos del juego se añaden al final del plan con presupuesto proporcional.
- **Un fallo de Steam al desbloquear** (rechazo) marca la entrada `Skipped`; un error de red la deja pendiente para el siguiente tick.
- **Errores aislados por juego en cada tick.** Una excepción al evaluar un juego, crear su plan o desbloquear un logro se registra y no afecta a otros juegos ni al resto del tick; un juego sin plan por error se reintenta pasados 30 minutos (`PlanRetryDelay`). Una excepción no controlada del tick se registra y el temporizador sigue.
- **Datos en `config/AchievementDrift/`:** `<bot>.config.json`, `<bot>.state.json` y `store-cache.json` (compartido por todos los bots). Nunca en la carpeta del plugin.
- **`DriftController` resuelve el bot desde el registro de ASF antes de tocar archivos.** `FindBot` obtiene el bot con `Bot.GetBot` y devuelve la instancia del registro interno (`Bot.BotsReadOnly`), de modo que el nombre que llega a las rutas de archivo nunca deriva directamente del texto de la petición (regla CA3003, path injection).
- **Exclusiones adicionales a las del spec.** `SafetyRules` también excluye juegos sin datos de tienda y juegos sin la categoría de logros (id 22), salvo que estén en `Allowlist`.
- **Concurrencia.** Cada `BotRunner` serializa tick, consultas de estado y `replan` con un `SemaphoreSlim`; un tick que no logra el candado en 30 s se omite.

## 4. Desarrollo local

Requisitos: **.NET 10 SDK** y git.

```bash
git clone --recursive https://github.com/owenunda/ASF-AchievementDrift.git
cd ASF-AchievementDrift
# si ya lo habías clonado sin --recursive:
git submodule update --init --recursive

dotnet build -c Release --nologo
dotnet test AchievementDrift.Tests -c Release --nologo
```

En Release el análisis estricto de ASF convierte los warnings en errores; compila en Release antes de publicar.

**Probar contra un ASF local:** la compilación del plugin copia su salida a `ArchiSteamFarm/ArchiSteamFarm/bin/<config>/net10.0/plugins/AchievementDrift/`. Compila también el proyecto `ArchiSteamFarm` (o ejecútalo con `dotnet run`) y arráncalo; el plugin se cargará desde esa carpeta. Empieza siempre con `DryRun = true`.

**Comprobar el paquete:** `dotnet publish AchievementDrift -c Release -o out/AchievementDrift --nologo`. La carpeta debe contener `AchievementDrift.dll` y `www/`, y **no** `ArchiSteamFarm.dll` ni `SteamKit2.dll` (si aparecen, revisa `IncludeAssets="compile"` / `Private="false"` en el `.csproj`). La carpeta `out/` está en `.gitignore`.

## 5. Publicar una versión

1. Sube `<Version>` en `Directory.Build.props` (formato `X.Y.Z.W`).
2. Haz commit.
3. Crea el tag `git tag X.Y.Z.W`; **debe coincidir con `Version`**.
4. `git push --tags`.
5. El workflow `.github/workflows/publish.yml` compila, genera `AchievementDrift.zip` y crea el release usando `.github/RELEASE_TEMPLATE.md` como cuerpo. Lo crea como **pre-release** y sin marcarlo como «latest» (`prerelease: true`, `makeLatest: false`).
6. **Promociona el release a mano.** Primero verifica el release: instala el zip en un ASF de prueba (con `DryRun = true`) y comprueba que carga y funciona. Después, en GitHub, edita el release, **desmarca «Set as a pre-release»** y **marca «Set as the latest release»**.
   - Hasta entonces, el release solo lo ven las instancias de ASF con `UpdateChannel` = `PreRelease` (esas piden el release más reciente aunque sea pre-release). Con el canal por defecto, `Stable`, ASF consulta `/releases/latest`, y GitHub excluye de «latest» los pre-releases.
   - La actualización automática además depende de la configuración global de ASF (`ASF.json`): `PluginsUpdateMode` (`Whitelist` por defecto) y `PluginsUpdateList` (vacía por defecto). Con esos valores ningún plugin se actualiza solo: el usuario debe añadir `AchievementDrift` (el nombre del ensamblado) a `PluginsUpdateList` en modo `Whitelist`, o usar el modo `Blacklist` (se actualizan todos los plugins que no estén en la lista).
7. Con el release promocionado y el plugin habilitado para actualizaciones, ASF lo actualiza por su cuenta gracias a `IGitHubPluginUpdates` (`RepositoryName = "owenunda/ASF-AchievementDrift"`).

## 6. Actualizar ASF

El workflow `bump-asf-reference.yml` (diario) mueve la referencia del submódulo de ASF a la última versión estable. Antes de publicar una versión nueva del plugin:

1. Revisa el changelog de ASF (las APIs internas que usamos — `CardsFarmer`, `ArchiWebHandler`/`WebBrowser`, `BotConfig` — pueden cambiar).
2. Comprueba que `dotnet build -c Release` y `dotnet test AchievementDrift.Tests -c Release` siguen en verde.
3. Prueba contra un ASF real con `DryRun = true`.
