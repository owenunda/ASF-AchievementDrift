# Documentación técnica — ASF-AchievementDrift

Guía para quien quiera entender, modificar o publicar el plugin. La documentación de uso está en el [README](../README.md); el diseño original en `docs/superpowers/specs/2026-10-14-achievement-drift-design.md`.

## 1. Arquitectura

Dos proyectos .NET (`net10.0`) basados en `JustArchiNET/ASF-PluginTemplate`, con ASF como submódulo git y central package management:

- `AchievementDrift`: el plugin (una DLL con la web estática en `www/`).
- `AchievementDrift.Tests`: tests MSTest (sin Steam ni ASF en ejecución).

### Unidades

| Unidad | Responsabilidad | Ubicación |
|---|---|---|
| `AchievementDriftPlugin` | Punto de entrada. `IGitHubPluginUpdates`, `IBot`, `IBotCommand2`, `IBotConnection`, `IBotSteamClient`, `IWebInterface`, `IWebServiceProvider`. Solo cablea las demás piezas; si el self-test falla no arranca el runtime y retira su controlador de MVC (ver §3). | `AchievementDriftPlugin.cs` |
| `StatsHandler` | `ClientMsgHandler` de SteamKit2: lee esquema y estado de logros (`ClientGetUserStats`) y desbloquea (`ClientStoreUserStats2`). | `Steam/StatsHandler.cs` |
| `SchemaParser` | Interpreta el esquema binario (KeyValues): nombre API, nombre visible, bit, protección y dependencias de progreso. | `Steam/SchemaParser.cs` |
| `WebApis` | Porcentajes globales (`GetGlobalAchievementPercentagesForApp/v2`, sin API key) y datos de tienda (`appdetails`). | `Steam/WebApis.cs` |
| `PercentCache` | Cache en memoria (24 h) de los porcentajes globales por appid. | `Storage/PercentCache.cs` |
| `StoreCache` | Cache persistente por appid de los datos de tienda (`store-cache.json`); los fallos de red se recuerdan 30 minutos en memoria. | `Storage/StoreCache.cs` |
| `ConfigStore` | Configuración por bot (`<bot>.config.json`); única fuente de configuración, con validación y cache en memoria (los cambios por web/IPC/comandos se aplican al instante). | `Storage/ConfigStore.cs` |
| `JsonFileStore` | Lectura/escritura JSON atómica en `config/AchievementDrift/`. | `Storage/JsonFileStore.cs` |
| `SafetyRules` | **Lógica pura.** Decide si un appid es elegible (Blacklist, Allowlist, VAC, online, sin logros, sin datos). | `Core/SafetyRules.cs` |
| `PlanGenerator` | **Lógica pura.** Crea y amplía planes (presupuesto, orden ponderado, espaciado). | `Core/PlanGenerator.cs` |
| `ActiveWindow` | **Lógica pura.** Franja `HH:mm-HH:mm` (puede cruzar la medianoche; inicio = fin es todo el día). Recibe un conversor UTC → hora local en vez de un `TimeZoneInfo`. | `Core/ActiveWindow.cs` |
| `TimeZones` | Único sitio que nombra `TimeZoneInfo`: busca zonas IANA y crea el conversor de `ActiveWindow`. Si la build no tiene zonas horarias (`Available = false`), usa la hora del sistema y `TimeZone` se rechaza al validar. | `Core/TimeZones.cs` |
| `DriftEngine` | **Lógica pura.** Un tick: huecos de juegos activos, minutos activos, siguiente desbloqueo. | `Core/DriftEngine.cs` |
| `StatusBuilder` | Construye el estado (`DriftStatus`) y su formato de texto para `!drift status` y la IPC. Los planes completados se guardan pero no se muestran. | `Core/StatusBuilder.cs` |
| `BotRunner` | Un temporizador de 1 minuto por bot conectado; orquesta lectura de juegos, motor, creación de planes, desbloqueo y guardado. | `Runtime/BotRunner.cs` |
| `DriftRuntime` | Registro estático de runners, handlers y almacenes; serializa arranque/parada de runners por bot. | `Runtime/DriftRuntime.cs` |
| `RuntimeSelfTest` | Al cargar, comprueba las APIs de la BCL que usa el plugin. Sin zonas horarias pone `TimeZones.Available = false`; si falta algo imprescindible, marca el plugin como no soportado (`Supported = false`). | `Runtime/RuntimeSelfTest.cs` |
| `DriftCommands` | Comandos `!drift status\|on\|off [bots]` y `!drift plan\|replan [bots] <appid>`. Resuelve los bots como ASF (`Bot.GetBots`) y comprueba `Master` en cada uno (`Commands.GetProxyAccess`). | `Commands/DriftCommands.cs` |
| `DriftController` | Endpoints IPC (`ArchiController`). | `IPC/DriftController.cs` |
| `www/` | Página `/drift/` (HTML/CSS/JS sin dependencias externas): **Estado** con la etiqueta del modo, **Configuración** agrupada (Funcionamiento / Ritmo / Qué juegos, modo Simulación/Real como dos opciones, horario con dos `input type=time`) y **Ayuda**. Textos en `i18n.js`. | `www/` |

El motor es puro y testeable: `DriftEngine.Tick` no toca Steam ni el disco; recibe el estado, la hora, los juegos elegibles en reproducción, los juegos en espera por errores y la configuración, y devuelve qué hay que crear/desbloquear. Los efectos secundarios viven en `BotRunner`.

### Flujo de un tick (cada minuto, por bot)

1. `BotRunner.TickAsync` toma el candado del estado y comprueba que el bot sigue conectado.
2. **Lectura de juegos:** si `CardsFarmer.NowFarming`, los juegos del farmeo; si no y `IsPlayingPossible`, `GamesPlayedWhileIdle`.
3. **`SafetyRules`:** cada juego se evalúa (consultando `StoreCache` si hace falta) y solo pasan los elegibles. Un fallo en un juego se registra y se trata como no elegible en ese tick.
4. **`DriftEngine.Tick`:** descarta los planes con simulaciones si ya no estamos en DryRun, comprueba `Enabled` y la franja horaria, libera/rellena huecos (`MaxActiveGames`; los juegos en espera no ocupan hueco; un juego empezado que se está jugando pero no consigue hueco pierde su plan, y los de juegos que ahora no se juegan se conservan), suma un minuto activo a cada juego activo con plan y devuelve los juegos que necesitan plan y, como máximo, un desbloqueo vencido de un juego que no esté en espera ni haya desbloqueado hace menos de 15 minutos activos.
5. **Crear planes:** para cada juego sin plan se lee el esquema (`StatsHandler`) y los porcentajes (`PercentCache`) y se llama a `PlanGenerator.Create`. Si falla, el juego entra en espera 30 minutos.
6. **Desbloquear:** en DryRun se registra `[AchievementDrift][DryRun] Desbloquearía ...` y la entrada pasa a `Simulated`; en modo real se sincroniza el plan con Steam (logros hechos fuera, logros nuevos), se valida que el logro exista y no sea protegido y se envía `ClientStoreUserStats2`. Si Steam no responde o da un error temporal, el juego entra en espera 30 minutos.
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
- `GET /Api/Plugins` devuelve `WebPath` de los plugins con `IWebInterface` a partir de la versión de ASF posterior a
  6.3.10.6 ([ArchiSteamFarm#3631](https://github.com/JustArchiNET/ArchiSteamFarm/issues/3631); antes solo daba `Name` y
  `Version`). ASF-ui lo usa para el enlace «Open web interface» de su página Plugins
  ([ASF-ui#1771](https://github.com/JustArchiNET/ASF-ui/pull/1771)). El plugin no necesita cambios para eso.
- La configuración se guarda con **POST** `/Api/Drift/{bot}/Config` (antes PUT): `HttpPutAttribute` no existe en las builds
  recortadas y su ausencia rompía todo el IPC de ASF (ver §3).

### Build
- Target `net10.0`; `Directory.Build.props` de ASF activa `AnalysisMode=AllEnabledByDefault` y
  `TreatWarningsAsErrors` en Release. El código sigue el estilo de ASF (`ConfigureAwait(false)`, cultura invariante).
- Las builds por SO de ASF (`win-x64`, `linux-x64`…) se publican con `PublishTrimmed=true` (`TrimMode=partial`) y
  single-file: falta todo lo de la BCL y de ASP.NET que ASF no usa. La imagen de Docker Hub (`justarchi/archisteamfarm`) se
  construye con `Dockerfile.Service` (también recortada). El plugin **funciona en ellas** desde 1.3.0.0, evitando lo que
  falta (6.3.10.6: probado en ejecución en win-x64; en linux-x64, con la comparación de referencias descrita abajo):

  | Falta en la build recortada | Se usa en su lugar |
  |---|---|
  | `TimeZoneInfo` (el reenvío de `System.Runtime`; el tipo sigue en CoreLib pero el plugin no puede nombrarlo) y `FindSystemTimeZoneById` | `TimeZones` con hora del sistema (`DateTime.ToLocalTime`) |
  | `Enumerable.MinBy`, `Enumerable.Max(selector long)`, `Enumerable.Sum(IEnumerable<double>)` | `MaxBy` (con clave negada para el mínimo), bucles |
  | `Math.Clamp(double)` | `Math.Min`/`Math.Max` |
  | `JsonElement.TryGetProperty`, `JsonProperty.NameEquals` | `EnumerateObject` + `JsonProperty.Name` |
  | `SemaphoreSlim.WaitAsync(TimeSpan)` | `WaitAsync(int)` |
  | `HttpPutAttribute`, `ControllerBase.Request`, `IHeaderDictionary.AcceptLanguage` | `HttpPost`, `HttpContext.Request.Headers["Accept-Language"]` |

  Cómo comprobarlo: el `.exe`/binario de ASF es un bundle single-file; se extraen sus ensamblados (cabecera del bundle
  `.NET` v6, entradas comprimidas con deflate) y se compara cada `TypeRef`/`MemberRef` de `AchievementDrift.dll`, con su
  firma exacta, contra ellos usando `System.Reflection.Metadata` + `MetadataLoadContext`. Comparar solo nombre y número de
  parámetros no basta (se escapa, por ejemplo, `Math.Clamp(double)`). Los únicos «faltantes» aceptables son los tipos de
  zona horaria, que solo se alcanzan con `TimeZones.Available = true`.
- La plantilla oficial ya apunta el submódulo de ASF a `6.3.10.5`.

## 3. Decisiones

- **MSTest en lugar de xUnit.** ASF ya fija la versión de MSTest en su central package management; usar lo mismo evita conflictos de versiones.
- **Configuración solo en el plugin.** Vive en `config/AchievementDrift/<bot>.config.json` y no en el JSON del bot de ASF, para tener una única fuente editable desde la web, la IPC y los comandos, con cambios al instante. `ConfigStore` cachea la configuración en memoria: editar el archivo en disco con ASF en marcha **no** se aplica (hace falta reiniciar ASF) y el siguiente guardado desde la web/IPC/comandos lo sobrescribe.
- **`!play` no soportado.** ASF no guarda qué juegos se le pasan (ver §2), así que no hay forma fiable de saber qué se está jugando.
- **Estado `Simulated` y regeneración al salir de DryRun.** En DryRun el desbloqueo no se envía: la entrada pasa a `Simulated`. Mientras dura el DryRun, el estado (`status`/web) muestra las entradas `Simulated` como hechas para que se vea el avance simulado, pero en Steam no se ha obtenido nada. Los planes se marcan con `CreatedInDryRun`; al desactivar `DryRun`, `DriftEngine` descarta esos planes **y también cualquier plan creado en modo real que tenga al menos una entrada `Simulated`** (caso real → DryRun → real), y se regeneran desde el estado real de Steam con un presupuesto proporcional a lo que falta.
- **Logros desbloqueados fuera del plugin** se marcan `Unlocked` al sincronizar el plan con Steam (justo antes de cada desbloqueo real). Los logros nuevos del juego se añaden al final del plan con presupuesto proporcional.
- **Un fallo de Steam al desbloquear** (rechazo) marca la entrada `Skipped`. Los `EResult` transitorios (`Busy`, `ServiceUnavailable`, `Timeout`, `RateLimitExceeded`, `TryAnotherCM`, `NoConnection`) y los errores de red/timeout no la saltan: `StatsHandler.UnlockAsync` devuelve `null` y se reintenta (ver siguiente punto). `Fail` y el resto cuentan como rechazo.
- **Errores aislados por juego en cada tick (espera por juego).** Una excepción al evaluar un juego se registra y lo trata como no elegible en ese tick. Si falla la creación del plan o el camino de desbloqueo (`GetAsync`/`UnlockAsync` sin respuesta o con excepción), `BotRunner` pone el juego **en espera 30 minutos** (`RetryDelay`) y pasa ese conjunto a `DriftEngine.Tick`: un juego en espera libera su hueco (su plan queda en pausa, sin sumar minutos) y nunca se devuelve como desbloqueo, así un juego atascado no bloquea a los demás. Tras 5 fallos seguidos del mismo logro, este se marca `Skipped` con un aviso. Si no se pueden obtener los porcentajes globales al crear un plan, se reintenta (con espera) hasta 3 veces; al tercer fallo seguido se crea el plan con `NaN` (50 % vía `NormalizePercent`, spec §3.1). Los contadores y esperas viven solo en memoria (se reinician con el runner o con `replan`). Una excepción no controlada del tick se registra y el temporizador sigue.
- **Datos en `config/AchievementDrift/`:** `<bot>.config.json`, `<bot>.state.json` y `store-cache.json` (compartido por todos los bots). Nunca en la carpeta del plugin.
- **`DriftController` resuelve el bot desde el registro de ASF antes de tocar archivos.** `FindBot` obtiene el bot con `Bot.GetBot` y devuelve la instancia del registro interno (`Bot.BotsReadOnly`), de modo que el nombre que llega a las rutas de archivo nunca deriva directamente del texto de la petición (regla CA3003, path injection).
- **Exclusiones adicionales a las del spec.** `SafetyRules` también excluye juegos sin datos de tienda y juegos sin la categoría de logros (id 22), salvo que estén en `Allowlist`.
- **VAC gana a Allowlist (decisión de la revisión final).** Orden de `SafetyRules.Evaluate`: Blacklist → VAC (si hay datos de tienda, `ExcludeVac` y categoría 8) → Allowlist → sin datos → online → sin logros → elegible. Por eso `NeedsStoreInfo` devuelve `true` para juegos en Allowlist cuando `ExcludeVac` está activado (hay que consultar la tienda para ver el VAC) y `false` solo para juegos en Blacklist o en Allowlist con `ExcludeVac=false`. Un juego en Allowlist sin datos de tienda sigue siendo elegible.
- **Hueco mínimo por juego entre desbloqueos.** `GamePlan.LastUnlockActiveMinutes` (persistido; `null` = ninguno) guarda los minutos activos del plan en el último desbloqueo real o simulado (`DriftEngine.RecordUnlock`). `FindDue` salta los juegos con `ActiveMinutes - LastUnlockActiveMinutes < PlanGenerator.MinGapMinutes` (15): aunque se acumulen varios logros vencidos (pausas, cambios de horario), un juego no desbloquea en ráfaga. Los logros que Steam ya marca como hechos al sincronizar no cuentan.
- **Idiomas (inglés/español).** `Core/Texts.cs` contiene todos los textos del backend en dos diccionarios con claves `ETextKey` (un test exige las mismas claves y los mismos huecos `{n}` en ambos). El idioma sale de `CultureInfo.CurrentUICulture`: español si es `es-*`, inglés en cualquier otro caso. Comandos y log usan la cultura de ASF (`CurrentCulture` de `ASF.json`); `DriftController` fija la cultura de cada petición con la cabecera `Accept-Language` que envía el panel (vive en el contexto asíncrono de la petición, no afecta a ASF). `SchemaParser` elige el idioma de nombres/descripciones de logros con la misma cultura, y la caché de la vista de logros va por idioma. El panel tiene su propio catálogo en `www/i18n.js` (selector EN/ES, `data-i18n*` en el HTML, `t()` en JS) y la Ayuda en dos bloques `data-lang`. Se eligió un catálogo en C# en lugar de `.resx` para que la lógica pura siga compilándose directamente en los tests sin generadores.
- **Builds recortadas: compatibles y sin romper ASF.** `RuntimeSelfTest.Run` (desde `OnLoaded`) ejecuta cada API sensible dentro de su propia lambda con `try/catch`: el `MissingMethodException`/`TypeLoadException` se lanza al compilar con JIT el método que usa el miembro, así que cada prueba va en un método aparte y **devuelve solo tipos básicos** (si devolviera, por ejemplo, un `TimeZoneInfo`, la instanciación genérica de `Check<T>` haría fallar el JIT del propio `Run`). Hay dos niveles:
  - Sin zonas horarias: `TimeZones.Available = false` y un aviso en el log. El plugin funciona con la hora del sistema; todo método que nombra `TimeZoneInfo` vive en `TimeZones`, separado y con `NoInlining`, para que el JIT no lo resuelva si no se llama.
  - Si falta algo imprescindible (red de seguridad ante recortes futuros de ASF): `Supported = false`, error en el log, el runtime no arranca y `!drift` responde con el aviso. Además `OnConfiguringEndpoints` (`IWebServiceProvider`, llamado justo antes de `MapControllers`) quita el ensamblado del plugin de `ApplicationPartManager`: MVC lee los atributos de todos los controladores al arrancar y un tipo ausente tumbaría **todo** el IPC de ASF, no solo la API del plugin.
- **Juegos sin hueco pierden el plan.** Un hueco solo se libera cuando un juego completa su plan. Si un juego empezado se está jugando pero no consigue hueco (por ejemplo, tras bajar `MaxActiveGames`), `DriftEngine.DropLeftoverPlans` borra su plan después de repartir los huecos (los empezados tienen prioridad); si más adelante consigue hueco, se crea uno nuevo desde Steam. Solo cuenta lo que se está jugando y no está en espera: los planes de juegos que ASF deja de jugar un rato (farmeo de cromos, errores) se conservan.
- **Comandos para varios bots.** Sin `[bots]` actúan sobre el bot que recibe el comando (como antes). Con `[bots]`, cada bot responde en sus líneas en paralelo (`Utilities.InParallel`) y en `status` la línea del panel se añade una sola vez al final (`StatusBuilder.FormatText(withPanel: false)` + `PanelLine`). En `plan`/`replan` el appid es el último argumento; lo que hay entre el subcomando y él son los bots.
- **Panel: decisiones de usabilidad.** El modo se muestra como dos opciones (Simulación / Real) en lugar de un checkbox «DryRun» en negativo, y como etiqueta de color al principio de Estado (verde Real, morado Simulación, gris Desactivado). La configuración se agrupa en tres bloques con una línea de ayuda por campo. `MinDays`/`MaxDays` se editan en una sola frase («entre X y Y días») y `ActiveHours` con dos `input type=time` (el formulario los junta en `HH:mm-HH:mm`); una frase calculada explica la franja («15 h al día: de … a … del día siguiente») en el mismo formato de 12/24 h que muestran los selectores. Los campos vacíos no llevan `placeholder` de ejemplo, porque parecían valores ya escritos.
- **Concurrencia.** Cada `BotRunner` serializa tick, consultas de estado y `replan` con un `SemaphoreSlim`; un tick que no logra el candado en 30 s se omite. `DriftRuntime` serializa `StartAsync`/`StopAsync` por bot (un `SemaphoreSlim` por nombre de bot) para que un logon y una desconexión solapados no dejen un runner huérfano. `BotRunner.DisposeAsync` desecha el temporizador, espera a que termine el tick en curso y marca el runner como desechado: desde entonces no guarda ni hace `replan`, así no pisa el estado que cargue el runner nuevo. No retiene el candado para no colgar consultas de estado que aún tengan una referencia al runner viejo.
- **Escritura atómica.** `JsonFileStore.SaveAsync` escribe a un temporal con nombre único (`<archivo>.<guid>.tmp`), lo mueve sobre el destino y lo borra si algo falla.

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

**Probar en una build recortada:** descarga `ASF-win-x64.zip` o `ASF-linux-x64.zip` del release de ASF, copia el plugin en `plugins/AchievementDrift/` y arráncalo. En el log debe salir el aviso de zonas horarias (es normal) y **no** el error «Your ASF build looks trimmed»; el IPC tiene que responder y `/drift/` funcionar.

**Comprobar el paquete:** `dotnet publish AchievementDrift -c Release -o out/AchievementDrift --nologo`. La carpeta debe contener `AchievementDrift.dll` y `www/`, y **no** `ArchiSteamFarm.dll` ni `SteamKit2.dll` (si aparecen, revisa `IncludeAssets="compile"` / `Private="false"` en el `.csproj`). La carpeta `out/` está en `.gitignore`.

## 5. Publicar una versión

1. Sube `<Version>` en `Directory.Build.props` (formato `X.Y.Z.W`) y haz commit.
2. `git push` a `main`.
3. El workflow `.github/workflows/auto-release.yml` detecta el cambio en `Directory.Build.props`. Si todavía no existe un tag con ese número, lo crea (`X.Y.Z.W`) y lanza `.github/workflows/publish.yml` sobre él (con `workflow_dispatch`, porque los tags creados con `GITHUB_TOKEN` no disparan otros workflows). Si el tag ya existe, no hace nada.
   - Sigue funcionando el camino manual: crear y subir tú el tag (`git tag -a X.Y.Z.W -m ...` + `git push origin X.Y.Z.W`) dispara `publish.yml` directamente.
4. `publish.yml` compila, genera `AchievementDrift.zip` y crea el release usando `.github/RELEASE_TEMPLATE.md` como cuerpo. Lo publica directamente como **estable** y lo marca como **«latest»** (`prerelease: false`, `makeLatest: true`), así que aparece como la última versión en GitHub y ASF (canal `Stable`, que consulta `/releases/latest`) lo ve de inmediato.
   - Por eso, **prueba antes de subir la versión**: instala el resultado de `dotnet publish` en un ASF de prueba (con `DryRun = true`) y comprueba que carga y funciona.
   - Si quieres publicar una versión de prueba, edita el release en GitHub y marca «Set as a pre-release»: solo la verán las instancias de ASF con `UpdateChannel` = `PreRelease`.
5. Elegir el número de versión es manual a propósito: publicar en cada push generaría un release por cada cambio pequeño.

6. La actualización automática además depende de la configuración global de ASF (`ASF.json`): `PluginsUpdateMode` (`Whitelist` por defecto) y `PluginsUpdateList` (vacía por defecto). Con esos valores ningún plugin se actualiza solo: el usuario debe añadir `AchievementDrift` (el nombre del ensamblado) a `PluginsUpdateList` en modo `Whitelist`, o usar el modo `Blacklist` (se actualizan todos los plugins que no estén en la lista).
7. Con el plugin habilitado para actualizaciones, ASF lo actualiza por su cuenta gracias a `IGitHubPluginUpdates` (`RepositoryName = "owenunda/ASF-AchievementDrift"`).

## 6. Actualizar ASF

El workflow `bump-asf-reference.yml` (diario) mueve la referencia del submódulo de ASF a la última versión estable. Antes de publicar una versión nueva del plugin:

1. Revisa el changelog de ASF (las APIs internas que usamos — `CardsFarmer`, `ArchiWebHandler`/`WebBrowser`, `BotConfig` — pueden cambiar).
2. Comprueba que `dotnet build -c Release` y `dotnet test AchievementDrift.Tests -c Release` siguen en verde.
3. Prueba contra un ASF real con `DryRun = true`.
