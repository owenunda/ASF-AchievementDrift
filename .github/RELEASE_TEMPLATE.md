### ASF-AchievementDrift

[ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm) plugin that unlocks achievements gradually and realistically while you idle hours.
*Plugin para ArchiSteamFarm que desbloquea logros de forma gradual y realista mientras farmeas horas.*

### Installation · Instalación

1. Download the zip for your ASF version: `AchievementDrift-V7.zip` for ASF 7.x, `AchievementDrift-V6.zip` for ASF 6.x. · *Descarga el zip de tu versión de ASF: `AchievementDrift-V7.zip` para ASF 7.x, `AchievementDrift-V6.zip` para ASF 6.x.*
2. Extract it into `plugins/AchievementDrift/` inside your ASF folder. · *Extráelo en `plugins/AchievementDrift/` dentro de tu carpeta de ASF.*
3. Restart ASF. · *Reinicia ASF.*
4. Open `https://<your-asf>/drift/` (with the trailing slash), enter your `IPCPassword` and review the configuration. It starts in **Simulation** mode: nothing is unlocked on Steam until you switch to **Real**. · *Abre `https://<tu-asf>/drift/` (con la barra final), introduce tu `IPCPassword` y revisa la configuración. Empieza en modo **Simulación**: no se desbloquea nada en Steam hasta que pases a **Real**.*

Works with the ASF **generic** build and the OS-specific builds (`win-x64`, `linux-x64`…). On the OS-specific builds and the official Docker image (trimmed) `ActiveHours` follow the system time zone and the `TimeZone` setting is not available. · *Funciona con la build **generic** de ASF y con las builds por SO (`win-x64`, `linux-x64`…). En las builds por SO y la imagen oficial de Docker (recortadas) `ActiveHours` sigue la zona horaria del sistema y la opción `TimeZone` no está disponible.*

With automatic plugin updates on, ASF picks the right zip by itself, and switches to the V7 one just before it updates itself to V7. · *Con las actualizaciones automáticas de plugins activadas, ASF elige solo el zip correcto y cambia al de V7 justo antes de actualizarse a V7.*

More info: [README (English)](https://github.com/owenunda/ASF-AchievementDrift#readme) · [README (español)](https://github.com/owenunda/ASF-AchievementDrift/blob/main/README.es.md)

### Warning · Aviso

Modifying achievements is against Steam's terms and carries risks, especially in games with anti-cheat or server-validated stats. Use it at your own risk. · *Modificar logros va contra los términos de Steam y puede tener riesgos, sobre todo en juegos con anti-cheat o estadísticas validadas en el servidor. Úsalo bajo tu responsabilidad.*
