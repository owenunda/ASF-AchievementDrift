# ASF-AchievementDrift

Plugin para [ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm) que desbloquea logros de forma gradual y realista mientras farmeas horas.

## ⚠️ Aviso importante

- **Modificar logros va contra los términos de Steam.** Úsalo bajo tu propia responsabilidad.
- Hay riesgo en juegos con anti-cheat (VAC) o con estadísticas validadas en el servidor. El plugin excluye por defecto los juegos con VAC y los online/multijugador, pero la detección no es perfecta (algunos juegos online no declaran sus categorías): usa `Blacklist` si dudas.
- `DryRun` está **activo por defecto**: el plugin solo simula y escribe en el log lo que haría. Debes desactivarlo tú a mano.

## Qué hace

- Los logros con menor % global de obtención tardan más y tienden a salir **al final**.
- Cada juego tarda **como mínimo `MinDays` días de calendario** (30 por defecto) en completarse.
- El orden y los tiempos de desbloqueo son **aleatorios**.
- Solo avanza en juegos que el bot **está jugando en ese momento** (`GamesPlayedWhileIdle` o el farmeo de cromos). Si un juego deja de jugarse, su progreso se pausa.
- Solo desbloquea dentro de un **horario humano** configurable, en tu zona horaria.
- Como máximo `MaxActiveGames` juegos (2 por defecto) están en progreso a la vez; el resto espera turno.
- Excluye automáticamente juegos con VAC y juegos online/multijugador, además de tu lista negra (`Blacklist`). Una lista blanca (`Allowlist`) fuerza la inclusión de juegos concretos.
- También se excluyen los juegos sin datos de tienda o sin la categoría «Logros de Steam»; la `Allowlist` los fuerza igualmente.

## Cómo funciona el algoritmo

El plan de un juego se crea la primera vez que un juego elegible aparece en reproducción:

1. **Candidatos:** los logros no desbloqueados y no protegidos, cada uno con su % global de obtención `p` (acotado a `[0,1 ; 100]`; si Steam no lo da se usa 50).
2. **Presupuesto:** `T = aleatorio(MinDays, MaxDays) × minutos de franja por día × (pendientes / total)`. En un día de calendario no caben más minutos activos que los de la franja, así que un juego empezado desde cero nunca se completa antes de `MinDays` días.
3. **Orden:** cada logro recibe la clave `k = U^(1/p)` con `U` uniforme en `(0,1)` y se ordena por `k` descendente (muestreo ponderado). Los comunes tienden a salir antes y los raros al final.
4. **Espaciado:** el hueco previo de cada logro es `(1/√p) × aleatorio(0,5 ; 1,5)`. Los huecos se escalan para que sumen `T`, con un mínimo de 15 minutos activos entre logros.

**Ejemplo:** horario `10:00-01:00` (15 h = 900 minutos al día), `MinDays = 30` y `MaxDays = 45`. El presupuesto de un juego nuevo está entre 30 × 900 = **27 000** y 45 × 900 = **40 500** minutos activos. Un logro del 2 % espera en promedio unas 6 veces más que uno del 80 % (`√(80/2) ≈ 6,3`), y el del 2 % sale después del del 80 % en aproximadamente el 97 % de los planes.

Cada minuto (un «tick») el plugin suma un minuto activo a los juegos en progreso y desbloquea, como máximo, **un logro por tick y por bot**: el pendiente más antiguo cuyo momento ya llegó.

## Requisitos

- ASF **6.3.10.5** o compatible.
- Se recomienda la build **`generic`** de ASF: las builds por sistema operativo (por ejemplo `linux-x64`) están recortadas y pueden dar `MissingMethodException`.
- El bot debe estar farmeando horas, normalmente con `GamesPlayedWhileIdle` (o farmeando cromos).

## Instalación

1. Descarga `AchievementDrift.zip` del [último release](https://github.com/owenunda/ASF-AchievementDrift/releases). Los releases marcados como *pre-release* son versiones de prueba.
2. Extrae el contenido en `plugins/AchievementDrift/` dentro de tu carpeta de ASF.
3. Reinicia ASF.
4. Comprueba en el log la línea `AchievementDrift ... cargado` y que el plugin aparece en la pestaña **Plugins** del panel de ASF.

**Docker:** copia o monta la carpeta extraída en `/app/plugins/AchievementDrift`. Para que la configuración y el progreso sobrevivan a recrear el contenedor, asegúrate de que `/app/config` también es persistente.

## Actualizaciones automáticas

Con los valores por defecto de ASF (`PluginsUpdateMode` = `Whitelist` y `PluginsUpdateList` vacía) **ningún plugin se actualiza solo**. Para activarlo en este plugin, añade `AchievementDrift` a `PluginsUpdateList` en `config/ASF.json` (manteniendo `Whitelist`) y reinicia ASF:

```json
{
  "PluginsUpdateMode": "Whitelist",
  "PluginsUpdateList": ["AchievementDrift"]
}
```

Con `UpdateChannel` = `Stable` (el valor por defecto) ASF solo toma los releases marcados como estables/«latest»; con `PreRelease` también toma las versiones de prueba.

## Primeros pasos

1. Abre `https://<tu-asf>/drift/` (**con la barra final**).
2. Introduce el `IPCPassword` de ASF (se guarda en el navegador).
3. Acepta la zona horaria que detecta la página, o ajusta la tuya.
4. Deja `DryRun` activo y revisa el log: verás líneas `[AchievementDrift][DryRun] Desbloquearía "..." ...`.
5. Cuando estés conforme, desactiva `DryRun`. Los planes creados durante el modo simulado se descartan y se vuelven a generar en modo real.

## Configuración

Cada bot tiene su archivo `config/AchievementDrift/<bot>.config.json`. Se edita desde la página web (`/drift/`) o por la API IPC, y los cambios se aplican **en caliente**, sin reiniciar. La configuración vive solo en este archivo (no en el JSON del bot de ASF).

| Clave | Por defecto | Descripción |
|---|---|---|
| `Enabled` | `true` | Activa el plugin para este bot. |
| `DryRun` | `true` | Simula sin desbloquear nada. Hay que desactivarlo a mano. |
| `MinDays` | `30` | Días mínimos para completar un juego. |
| `MaxDays` | `45` | Días máximos (techo del sorteo). Debe ser ≥ `MinDays`. |
| `ActiveHours` | `"10:00-01:00"` | Franja horaria `HH:mm-HH:mm`; puede cruzar la medianoche. |
| `TimeZone` | zona del servidor | Identificador IANA (por ejemplo `America/Bogota`). |
| `MaxActiveGames` | `2` | Juegos en progreso a la vez (1 a 10). |
| `ExcludeVac` | `true` | Excluye juegos con VAC. |
| `ExcludeOnline` | `true` | Excluye juegos con categorías online/multijugador. |
| `Blacklist` | `[]` | Appids excluidos siempre. |
| `Allowlist` | `[]` | Appids incluidos siempre (salvo que estén en `Blacklist`). |

Los valores inválidos se rechazan (la API responde 400 con un mensaje); si el archivo está corrupto se ignora con un aviso en el log y se usan los valores por defecto. Cambiar `MinDays`/`MaxDays` no regenera planes existentes, solo afecta a planes nuevos.

## Comandos

Requieren permiso `Master` y actúan sobre el bot que recibe el comando.

| Comando | Qué hace |
|---|---|
| `!drift status` | Progreso por juego, próximo logro y ETA. |
| `!drift on` / `!drift off` | Activa o desactiva el plugin para ese bot (`Enabled`). |
| `!drift plan <appid>` | Muestra el plan de un juego sin desbloquear nada. |
| `!drift replan <appid>` | Elimina el plan de un juego; se regenera cuando el juego vuelva a estar activo. |

## API IPC

Protegida por el `IPCPassword` de ASF y visible en Swagger. Las respuestas siguen el formato `GenericResponse` de ASF (propiedades en PascalCase).

| Método y ruta | Descripción |
|---|---|
| `GET /Api/Drift/Bots` | Nombres de los bots. |
| `GET /Api/Drift/{bot}` | Estado: juegos, logros obtenidos/total, % del plan, próximo logro. |
| `GET /Api/Drift/{bot}/Config` | Configuración del bot. |
| `PUT /Api/Drift/{bot}/Config` | Guarda la configuración (validada). |

```bash
curl -H "Authentication: <IPCPassword>" https://<tu-asf>/Api/Drift/<bot>
```

## Archivos que crea

Todo se guarda en `config/AchievementDrift/` (nunca en la carpeta del plugin, porque las actualizaciones de plugins la reemplazan):

- `<bot>.config.json`: configuración del bot.
- `<bot>.state.json`: planes y progreso del bot.
- `store-cache.json`: datos de la tienda (nombre, VAC, categorías) por appid.

Para **reiniciar el progreso** de un bot: detén ASF y borra su `<bot>.state.json`.

## Preguntas frecuentes

**¿Por qué no avanza?** Revisa que: (1) estés dentro del horario `ActiveHours` y de la zona `TimeZone`; (2) el juego no esté excluido (VAC, online o `Blacklist`); (3) el juego esté en `GamesPlayedWhileIdle` o se esté farmeando; (4) no haya ya `MaxActiveGames` juegos ocupando los huecos; (5) `Enabled` sea `true` y el bot esté conectado.

**¿Funciona si uso `!play`?** No. ASF no guarda qué juegos se le piden con `!play`, así que el plugin no puede detectarlos. Usa `GamesPlayedWhileIdle`.

**¿Por qué se excluyó un juego?** El log indica el motivo con una línea `[AchievementDrift] Se excluye ...`. Puedes forzar su inclusión con `Allowlist` o excluirlo siempre con `Blacklist`.

**¿Qué pasa con los logros protegidos?** Se saltan: nunca entran al plan. Si Steam rechaza un desbloqueo, ese logro se marca como saltado y se registra un aviso.

**Cambié el horario y ahora va más rápido.** Los planes se miden en minutos activos. Ampliar el horario acelera el calendario (más minutos activos por día); reducirlo lo alarga.

**La página `/drift` me lleva al panel de ASF o da 404.** La URL debe terminar con barra: `https://<tu-asf>/drift/`.

**El plugin no carga y veo `MissingMethodException`.** La build `linux-x64` de ASF está recortada. Usa la build `generic`.

## Compilar desde el código

```bash
git clone --recursive https://github.com/owenunda/ASF-AchievementDrift.git
cd ASF-AchievementDrift
dotnet test AchievementDrift.Tests
dotnet publish AchievementDrift -c Release -o out
```

Más detalles en [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Licencia

[Apache-2.0](LICENSE.txt).
