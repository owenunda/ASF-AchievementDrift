# ASF-AchievementDrift Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Plugin de ArchiSteamFarm que desbloquea logros de forma gradual, aleatoria y realista en los juegos que el bot está jugando, con comandos, IPC, página web y documentación.

**Architecture:** Núcleo de lógica pura (`AchievementDrift/Core`, sin dependencias de ASF) que genera planes y decide cada minuto qué hacer; capa de integración (`Steam/`, `Storage/`, `Runtime/`, `Commands/`, `IPC/`) que habla con SteamKit2, la Web API de Steam, el disco y ASF. Los tests compilan directamente los archivos del núcleo (y `SchemaParser.cs`), así que no necesitan ASF en tiempo de ejecución.

**Tech Stack:** C# / .NET 10 (`net10.0`), ArchiSteamFarm 6.3.10.5 (submódulo git), SteamKit2 3.4.0, ASP.NET Core (IPC de ASF), MSTest 4.4.1, HTML/CSS/JS sin dependencias.

**Spec:** `docs/superpowers/specs/2026-10-14-achievement-drift-design.md` (leer entero, en especial §11 "Notas técnicas").

## Global Constraints

- Target `net10.0`; ASF referenciado como submódulo en `ArchiSteamFarm/` fijado al tag `6.3.10.5`.
- Nombre del plugin / DLL / zip: `AchievementDrift`. Repo: `owenunda/ASF-AchievementDrift`. Licencia Apache-2.0.
- Paquetes ya provistos por ASF se referencian con `IncludeAssets="compile"` (SteamKit2, System.Composition.AttributedModel, Microsoft.AspNetCore.OpenApi). No se añaden paquetes nuevos al plugin.
- Datos y configuración en `config/AchievementDrift/` (nunca en la carpeta del plugin).
- JSON con `JsonUtilities.ToJsonText()` / `ToJsonObject<T>()` de ASF: propiedades en **PascalCase**. Nunca serializar `NaN`/`Infinity`.
- Estilo ASF: tabs, llaves siempre, `ConfigureAwait(false)` en cada `await` del plugin, `CultureInfo.InvariantCulture` al formatear/parsear números.
- La build `Release` de ASF trata warnings como errores (`AnalysisMode=AllEnabledByDefault`). Cada tarea que toca el plugin termina con `dotnet build -c Release` limpio.
- Textos para el usuario (logs, comandos, web, README) en **español**.
- Valores por defecto: `Enabled=true`, `DryRun=true`, `MinDays=30`, `MaxDays=45`, `ActiveHours="10:00-01:00"`, `TimeZone=null` (servidor), `MaxActiveGames=2`, `ExcludeVac=true`, `ExcludeOnline=true`, listas vacías.
- Hueco mínimo entre logros: 15 minutos activos. Máximo 1 desbloqueo por tick (1 minuto) y por bot.
- Categorías de tienda: VAC = 8; logros de Steam = 22; online = {1, 20, 27, 36, 38, 49}.
- URL de la web: `/drift/` (con barra final). Endpoints bajo `/Api/Drift`.

## File Structure

```
.gitmodules                         submódulo ArchiSteamFarm @ 6.3.10.5
ArchiSteamFarm/                     submódulo (no editar)
AchievementDrift.slnx               solución
Directory.Build.props               PluginName, versión, metadatos
Directory.Packages.props            importa versiones de ASF
.editorconfig .gitattributes .gitignore LICENSE.txt   (de la plantilla)
.github/workflows/ci.yml            build + tests
.github/workflows/publish.yml       release al subir tag
.github/workflows/bump-asf-reference.yml
AchievementDrift/
  AchievementDrift.csproj
  AchievementDriftPlugin.cs         punto de entrada (interfaces de ASF)
  Core/                             lógica pura, testeada
    DriftConfig.cs                  configuración + validación
    TimeZoneResolver.cs             búsqueda segura de zonas IANA
    ActiveWindow.cs                 franja horaria + ETA
    AchievementInfo.cs              entrada del generador
    StoreInfo.cs                    datos de tienda cacheados
    GamePlan.cs                     modelo persistido (plan, entradas, estado del bot)
    PlanGenerator.cs                algoritmo del plan
    DriftEngine.cs                  decisión de cada tick
    SafetyRules.cs                  elegibilidad (VAC/online/listas)
    DriftStatus.cs                  DTOs públicos de estado
    StatusBuilder.cs                construye estado + texto
  Steam/
    SchemaParser.cs                 KeyValues del esquema → logros (testeado)
    StatsHandler.cs                 ClientMsgHandler: leer/escribir stats
    WebApis.cs                      % globales + appdetails
  Storage/
    JsonFileStore.cs                lectura/escritura atómica en config/AchievementDrift
    ConfigStore.cs                  configuración por bot (caché + disco)
    StoreCache.cs                   caché persistente de appdetails
    PercentCache.cs                 caché en memoria (24 h) de % globales
  Runtime/
    DriftRuntime.cs                 registro estático de handlers/runners/stores
    BotRunner.cs                    timer por bot, une todo
  Commands/
    DriftCommands.cs                !drift status|on|off|plan|replan
  IPC/
    DriftController.cs              /Api/Drift
  www/
    index.html  app.js  style.css   página /drift/
AchievementDrift.Tests/
  AchievementDrift.Tests.csproj
  AssemblyInfo.cs
  DriftConfigTests.cs  ActiveWindowTests.cs  PlanGeneratorTests.cs
  DriftEngineTests.cs  SafetyRulesTests.cs   StatusBuilderTests.cs
  SchemaParserTests.cs
README.md                           documentación de usuario (español)
docs/DEVELOPMENT.md                 documentación técnica (notas de ASF, arquitectura, build, release)
```

**Rutas:** todos los comandos se ejecutan desde la raíz del repo `C:\Users\owenu\OneDrive\Desktop\Personal\steam_plugin_logros` (en Bash: `/c/Users/owenu/OneDrive/Desktop/Personal/steam_plugin_logros`). `$SCRATCH` = directorio scratchpad de la sesión.

---

### Task 1: SDK, andamiaje del repo y proyecto de tests

**Files:**
- Create: `.gitmodules`, `ArchiSteamFarm/` (submódulo), `AchievementDrift.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitattributes`, `.gitignore`, `LICENSE.txt`, `.github/workflows/{ci,publish,bump-asf-reference}.yml`
- Create: `AchievementDrift/AchievementDrift.csproj`, `AchievementDrift/AchievementDriftPlugin.cs`
- Create: `AchievementDrift.Tests/AchievementDrift.Tests.csproj`, `AchievementDrift.Tests/AssemblyInfo.cs`, `AchievementDrift.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: proyecto `AchievementDrift` (namespace raíz `AchievementDrift`) y proyecto `AchievementDrift.Tests` que compila `AchievementDrift/Core/**/*.cs` y `AchievementDrift/Steam/SchemaParser.cs`.

- [ ] **Step 1: Instalar el SDK de .NET 10 (pedir permiso al usuario antes)**

El equipo solo tiene el runtime 9.0. Pedir confirmación explícita y luego:

```powershell
winget install --id Microsoft.DotNet.SDK.10 --exact --accept-source-agreements --accept-package-agreements
```

Verificar en una terminal nueva: `dotnet --list-sdks` → debe listar `10.0.x`.

- [ ] **Step 2: Añadir el submódulo de ASF fijado a 6.3.10.5**

```bash
git config core.longpaths true
git -c core.longpaths=true submodule add https://github.com/JustArchiNET/ArchiSteamFarm.git ArchiSteamFarm
git -C ArchiSteamFarm config core.longpaths true
git -C ArchiSteamFarm checkout 6.3.10.5
git config -f .gitmodules submodule.ArchiSteamFarm.branch 6.3.10.5
```

Expected: `git -C ArchiSteamFarm describe --tags` → `6.3.10.5`.

- [ ] **Step 3: Copiar archivos de la plantilla oficial**

```bash
git clone --depth 1 https://github.com/JustArchiNET/ASF-PluginTemplate.git "$SCRATCH/template"
cp "$SCRATCH/template/.editorconfig" "$SCRATCH/template/.gitattributes" "$SCRATCH/template/.gitignore" "$SCRATCH/template/LICENSE.txt" "$SCRATCH/template/Directory.Packages.props" .
mkdir -p .github/workflows
cp "$SCRATCH/template/.github/workflows/ci.yml" "$SCRATCH/template/.github/workflows/publish.yml" "$SCRATCH/template/.github/workflows/bump-asf-reference.yml" .github/workflows/
```

- [ ] **Step 4: Crear `Directory.Build.props`**

```xml
<Project>
	<Import Project="ArchiSteamFarm/Directory.Build.props" />

	<PropertyGroup>
		<PluginName>AchievementDrift</PluginName>
		<Version>1.0.0.0</Version>
	</PropertyGroup>

	<PropertyGroup>
		<ApplicationIcon />
		<Authors>owenunda</Authors>
		<Company>$(Authors)</Company>
		<Copyright>Copyright © $([System.DateTime]::UtcNow.Year) $(Company)</Copyright>
		<Description>Plugin para ArchiSteamFarm que desbloquea logros de forma gradual y realista mientras farmeas horas.</Description>
		<PackageIcon />
		<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
		<PackageProjectUrl>https://github.com/owenunda/ASF-AchievementDrift</PackageProjectUrl>
		<PackageReleaseNotes>$(PackageProjectUrl)/releases</PackageReleaseNotes>
		<RepositoryUrl>$(PackageProjectUrl).git</RepositoryUrl>
	</PropertyGroup>

	<!-- Reset ASF signing settings, as we'll use our own logic -->
	<PropertyGroup>
		<AssemblyOriginatorKeyFile />
		<PublicSign>false</PublicSign>
		<SignAssembly>false</SignAssembly>
	</PropertyGroup>
</Project>
```

- [ ] **Step 5: Crear `AchievementDrift.slnx`**

```xml
<Solution>
	<Configurations>
		<BuildType Name="Debug" />
		<BuildType Name="DebugFast" />
		<BuildType Name="Release" />
	</Configurations>

	<Project Path="ArchiSteamFarm/ArchiSteamFarm/ArchiSteamFarm.csproj" />

	<Project Path="AchievementDrift/AchievementDrift.csproj" />
	<Project Path="AchievementDrift.Tests/AchievementDrift.Tests.csproj" />
</Solution>
```

- [ ] **Step 6: Crear `AchievementDrift/AchievementDrift.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<OutputType>Library</OutputType>
		<!-- Random se usa para variación de juego, no para criptografía -->
		<NoWarn>$(NoWarn);CA5394</NoWarn>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="JetBrains.Annotations.Sources" PrivateAssets="all" />
		<PackageReference Include="Microsoft.AspNetCore.OpenApi" IncludeAssets="compile" />
		<PackageReference Include="SteamKit2" IncludeAssets="compile" />
		<PackageReference Include="System.Composition.AttributedModel" IncludeAssets="compile" />
	</ItemGroup>

	<ItemGroup>
		<ProjectReference Include="..\ArchiSteamFarm\ArchiSteamFarm\ArchiSteamFarm.csproj" ExcludeAssets="all" Private="false" />
	</ItemGroup>

	<ItemGroup>
		<None Include="www\**\*" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
	</ItemGroup>

	<Target Name="PostBuild" AfterTargets="PostBuildEvent">
		<Copy SourceFolders="$(TargetDir)" DestinationFolder="..\ArchiSteamFarm\ArchiSteamFarm\bin\$(Configuration)\$(TargetFramework)\plugins\$(AssemblyName)\" SkipUnchangedFiles="true" />
	</Target>
</Project>
```

- [ ] **Step 7: Crear `AchievementDrift/AchievementDriftPlugin.cs` mínimo**

```csharp
using System;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using JetBrains.Annotations;

namespace AchievementDrift;

#pragma warning disable CA1812 // ASF uses this class during runtime
[UsedImplicitly]
internal sealed class AchievementDriftPlugin : IGitHubPluginUpdates {
	internal const string PluginName = "AchievementDrift";

	public string Name => PluginName;
	public string RepositoryName => "owenunda/ASF-AchievementDrift";
	public Version Version => typeof(AchievementDriftPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	public Task OnLoaded() {
		ASF.ArchiLogger.LogGenericInfo($"{Name} {Version} cargado.");

		return Task.CompletedTask;
	}
}
#pragma warning restore CA1812 // ASF uses this class during runtime
```

- [ ] **Step 8: Crear el proyecto de tests**

`AchievementDrift.Tests/AchievementDrift.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<OutputType>Library</OutputType>
		<AnalysisMode>AllDisabledByDefault</AnalysisMode>
		<IsPackable>false</IsPackable>
		<RootNamespace>AchievementDrift.Tests</RootNamespace>
	</PropertyGroup>

	<ItemGroup>
		<PackageReference Include="MSTest" />
		<PackageReference Include="SteamKit2" />
	</ItemGroup>

	<!-- Compilamos directamente la lógica pura: así los tests no necesitan ASF en ejecución -->
	<ItemGroup>
		<Compile Include="..\AchievementDrift\Core\**\*.cs" Link="Core\%(RecursiveDir)%(Filename)%(Extension)" />
		<Compile Include="..\AchievementDrift\Steam\SchemaParser.cs" Link="Steam\SchemaParser.cs" />
	</ItemGroup>
</Project>
```

`AchievementDrift.Tests/AssemblyInfo.cs`:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
```

`AchievementDrift.Tests/SmokeTests.cs`:

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class SmokeTests {
	[TestMethod]
	public void TestRunnerWorks() => Assert.IsTrue(true);
}
```

Un `Compile Include` explícito de un archivo inexistente rompe la build, así que se crea ya `AchievementDrift/Steam/SchemaParser.cs` como stub (se completa en la Task 8):

```csharp
namespace AchievementDrift.Steam;

internal static class SchemaParser {
}
```

- [ ] **Step 9: Añadir paso de tests al CI**

En `.github/workflows/ci.yml`, después del paso `Build ${{ matrix.configuration }}`, añadir:

```yaml
    - name: Test ${{ matrix.configuration }}
      run: dotnet test AchievementDrift.Tests -c "${{ matrix.configuration }}" -p:ContinuousIntegrationBuild=true -p:UseAppHost=false --nologo
```

- [ ] **Step 10: Compilar y ejecutar tests**

```bash
dotnet build AchievementDrift -c Release --nologo
dotnet test AchievementDrift.Tests --nologo
```

Expected: build sin errores ni warnings; `Passed: 1`.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "chore: scaffold plugin from ASF-PluginTemplate (ASF 6.3.10.5)"
```

---

### Task 2: `DriftConfig` + `TimeZoneResolver`

**Files:**
- Create: `AchievementDrift/Core/DriftConfig.cs`, `AchievementDrift/Core/TimeZoneResolver.cs`
- Create (stub para compilar, se completa en Task 3): `AchievementDrift/Core/ActiveWindow.cs`
- Test: `AchievementDrift.Tests/DriftConfigTests.cs`

**Interfaces:**
- Consumes: `ActiveWindow.TryParseHours(string? text, out TimeOnly start, out TimeOnly end)` (Task 3; en esta tarea se crea con su implementación final, ver Step 3).
- Produces:
  - `public sealed record DriftConfig` con props `init`: `bool Enabled`, `bool DryRun`, `int MinDays`, `int MaxDays`, `string ActiveHours`, `string? TimeZone`, `int MaxActiveGames`, `bool ExcludeVac`, `bool ExcludeOnline`, `ImmutableHashSet<uint> Blacklist`, `ImmutableHashSet<uint> Allowlist`.
  - `IReadOnlyList<string> DriftConfig.Validate()` (vacía = válida).
  - `TimeZoneInfo DriftConfig.ResolveTimeZone()`; `string DriftConfig.DescribeTimeZone()`.
  - `internal static bool TimeZoneResolver.TryFind(string? id, [NotNullWhen(true)] out TimeZoneInfo? timeZone)`.

- [ ] **Step 1: Escribir los tests**

`AchievementDrift.Tests/DriftConfigTests.cs`:

```csharp
using System;
using AchievementDrift.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class DriftConfigTests {
	[TestMethod]
	public void DefaultsAreValid() {
		DriftConfig config = new();

		Assert.AreEqual(0, config.Validate().Count);
		Assert.IsTrue(config.Enabled);
		Assert.IsTrue(config.DryRun);
		Assert.AreEqual(30, config.MinDays);
		Assert.AreEqual(45, config.MaxDays);
		Assert.AreEqual("10:00-01:00", config.ActiveHours);
		Assert.IsNull(config.TimeZone);
		Assert.AreEqual(2, config.MaxActiveGames);
		Assert.IsTrue(config.ExcludeVac);
		Assert.IsTrue(config.ExcludeOnline);
	}

	[TestMethod]
	[DataRow(0, 45)]
	[DataRow(366, 366)]
	[DataRow(30, 29)]
	[DataRow(30, 366)]
	public void InvalidDaysAreRejected(int minDays, int maxDays) {
		DriftConfig config = new() { MinDays = minDays, MaxDays = maxDays };

		Assert.IsTrue(config.Validate().Count > 0);
	}

	[TestMethod]
	[DataRow("25:00-01:00")]
	[DataRow("10:00")]
	[DataRow("abc")]
	[DataRow("")]
	public void InvalidHoursAreRejected(string hours) {
		DriftConfig config = new() { ActiveHours = hours };

		Assert.IsTrue(config.Validate().Count > 0);
	}

	[TestMethod]
	public void InvalidTimeZoneIsRejected() {
		DriftConfig config = new() { TimeZone = "Mars/Phobos" };

		Assert.IsTrue(config.Validate().Count > 0);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(11)]
	public void InvalidMaxActiveGamesIsRejected(int value) {
		DriftConfig config = new() { MaxActiveGames = value };

		Assert.IsTrue(config.Validate().Count > 0);
	}

	[TestMethod]
	public void ZeroAppIdInListsIsRejected() {
		Assert.IsTrue(new DriftConfig { Blacklist = [0] }.Validate().Count > 0);
		Assert.IsTrue(new DriftConfig { Allowlist = [0] }.Validate().Count > 0);
	}

	[TestMethod]
	public void ResolveTimeZoneFallsBackToLocal() {
		Assert.AreEqual(TimeZoneInfo.Local.Id, new DriftConfig().ResolveTimeZone().Id);
	}

	[TestMethod]
	public void ResolveTimeZoneUsesIanaId() {
		TimeZoneInfo timeZone = new DriftConfig { TimeZone = "America/Bogota" }.ResolveTimeZone();

		Assert.AreEqual(TimeSpan.FromHours(-5), timeZone.BaseUtcOffset);
	}
}
```

- [ ] **Step 2: Ejecutar y ver que falla**

Run: `dotnet test AchievementDrift.Tests --filter "FullyQualifiedName~DriftConfigTests" --nologo`
Expected: error de compilación (`DriftConfig` no existe).

- [ ] **Step 3: Implementar**

`AchievementDrift/Core/TimeZoneResolver.cs`:

```csharp
using System;
using System.Diagnostics.CodeAnalysis;

namespace AchievementDrift.Core;

internal static class TimeZoneResolver {
	internal static bool TryFind(string? id, [NotNullWhen(true)] out TimeZoneInfo? timeZone) {
		timeZone = null;

		if (string.IsNullOrWhiteSpace(id)) {
			return false;
		}

		try {
			timeZone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());

			return true;
		} catch (TimeZoneNotFoundException) {
			return false;
		} catch (InvalidTimeZoneException) {
			return false;
		}
	}
}
```

`AchievementDrift/Core/ActiveWindow.cs` (implementación completa; sus tests llegan en la Task 3):

```csharp
using System;
using System.Globalization;

namespace AchievementDrift.Core;

internal sealed class ActiveWindow {
	internal const int MinutesInDay = 24 * 60;
	private const int MaxEstimateMinutes = 400 * MinutesInDay;
	private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];

	internal TimeOnly Start { get; }
	internal TimeOnly End { get; }
	internal TimeZoneInfo TimeZone { get; }

	internal int MinutesPerDay => Start == End ? MinutesInDay : (int) (End - Start).TotalMinutes;

	internal ActiveWindow(TimeOnly start, TimeOnly end, TimeZoneInfo timeZone) {
		ArgumentNullException.ThrowIfNull(timeZone);

		Start = start;
		End = end;
		TimeZone = timeZone;
	}

	internal static ActiveWindow FromConfig(DriftConfig config) {
		ArgumentNullException.ThrowIfNull(config);

		if (!TryParseHours(config.ActiveHours, out TimeOnly start, out TimeOnly end)) {
			throw new ArgumentException(config.ActiveHours, nameof(config));
		}

		return new ActiveWindow(start, end, config.ResolveTimeZone());
	}

	internal static bool TryParseHours(string? text, out TimeOnly start, out TimeOnly end) {
		start = end = default;

		if (string.IsNullOrWhiteSpace(text)) {
			return false;
		}

		string[] parts = text.Split('-');

		return (parts.Length == 2) &&
			TimeOnly.TryParseExact(parts[0].Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out start) &&
			TimeOnly.TryParseExact(parts[1].Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out end);
	}

	internal bool Contains(DateTime utcNow) {
		if (Start == End) {
			return true;
		}

		DateTime local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), TimeZone);

		return TimeOnly.FromDateTime(local).IsBetween(Start, End);
	}

	// Instante UTC en el que se habrán acumulado activeMinutes minutos dentro de la franja, o null si excede el límite
	internal DateTime? EstimateUtc(DateTime fromUtc, long activeMinutes) {
		if (activeMinutes <= 0) {
			return fromUtc;
		}

		DateTime cursor = new(fromUtc.Ticks - (fromUtc.Ticks % TimeSpan.TicksPerMinute), DateTimeKind.Utc);
		long remaining = activeMinutes;

		for (int i = 0; i < MaxEstimateMinutes; i++) {
			cursor = cursor.AddMinutes(1);

			if (Contains(cursor) && (--remaining == 0)) {
				return cursor;
			}
		}

		return null;
	}
}
```

`AchievementDrift/Core/DriftConfig.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace AchievementDrift.Core;

public sealed record DriftConfig {
	public const int MaxAllowedActiveGames = 10;
	public const int MaxAllowedDays = 365;

	public bool Enabled { get; init; } = true;
	public bool DryRun { get; init; } = true;
	public int MinDays { get; init; } = 30;
	public int MaxDays { get; init; } = 45;
	public string ActiveHours { get; init; } = "10:00-01:00";
	public string? TimeZone { get; init; }
	public int MaxActiveGames { get; init; } = 2;
	public bool ExcludeVac { get; init; } = true;
	public bool ExcludeOnline { get; init; } = true;
	public ImmutableHashSet<uint> Blacklist { get; init; } = [];
	public ImmutableHashSet<uint> Allowlist { get; init; } = [];

	public string DescribeTimeZone() => TimeZone ?? $"{TimeZoneInfo.Local.Id} (servidor)";

	public TimeZoneInfo ResolveTimeZone() => TimeZoneResolver.TryFind(TimeZone, out TimeZoneInfo? timeZone) ? timeZone : TimeZoneInfo.Local;

	public IReadOnlyList<string> Validate() {
		List<string> errors = [];

		if (MinDays is < 1 or > MaxAllowedDays) {
			errors.Add($"{nameof(MinDays)} debe estar entre 1 y {MaxAllowedDays}.");
		}

		if ((MaxDays < MinDays) || (MaxDays > MaxAllowedDays)) {
			errors.Add($"{nameof(MaxDays)} debe estar entre {nameof(MinDays)} y {MaxAllowedDays}.");
		}

		if (!ActiveWindow.TryParseHours(ActiveHours, out _, out _)) {
			errors.Add($"{nameof(ActiveHours)} debe tener el formato HH:mm-HH:mm (por ejemplo 10:00-01:00).");
		}

		if ((TimeZone != null) && !TimeZoneResolver.TryFind(TimeZone, out _)) {
			errors.Add($"{nameof(TimeZone)} '{TimeZone}' no es una zona horaria válida (usa un identificador IANA, por ejemplo America/Bogota).");
		}

		if (MaxActiveGames is < 1 or > MaxAllowedActiveGames) {
			errors.Add($"{nameof(MaxActiveGames)} debe estar entre 1 y {MaxAllowedActiveGames}.");
		}

		if (Blacklist.Contains(0) || Allowlist.Contains(0)) {
			errors.Add("Las listas no pueden contener el appid 0.");
		}

		return errors;
	}
}
```

- [ ] **Step 4: Ejecutar tests**

Run: `dotnet test AchievementDrift.Tests --filter "FullyQualifiedName~DriftConfigTests" --nologo`
Expected: PASS (todos).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add DriftConfig with validation and time zone resolution"
```

---

### Task 3: Tests de `ActiveWindow`

**Files:**
- Test: `AchievementDrift.Tests/ActiveWindowTests.cs`
- Modify (solo si un test falla): `AchievementDrift/Core/ActiveWindow.cs`

**Interfaces:**
- Produces (ya implementado en Task 2): `ActiveWindow(TimeOnly start, TimeOnly end, TimeZoneInfo tz)`, `static ActiveWindow FromConfig(DriftConfig)`, `static bool TryParseHours(...)`, `int MinutesPerDay`, `bool Contains(DateTime utcNow)`, `DateTime? EstimateUtc(DateTime fromUtc, long activeMinutes)`, `const int MinutesInDay = 1440`.

- [ ] **Step 1: Escribir los tests**

```csharp
using System;
using AchievementDrift.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class ActiveWindowTests {
	private static ActiveWindow Utc(string hours) {
		Assert.IsTrue(ActiveWindow.TryParseHours(hours, out TimeOnly start, out TimeOnly end));

		return new ActiveWindow(start, end, TimeZoneInfo.Utc);
	}

	private static DateTime At(int year, int month, int day, int hour, int minute) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

	[TestMethod]
	[DataRow("10:00-01:00", 900)]
	[DataRow("9:30-17:45", 495)]
	[DataRow("00:00-00:00", 1440)]
	public void MinutesPerDay(string hours, int expected) => Assert.AreEqual(expected, Utc(hours).MinutesPerDay);

	[TestMethod]
	[DataRow(23, 0, true)]
	[DataRow(0, 30, true)]
	[DataRow(1, 0, false)]
	[DataRow(9, 59, false)]
	[DataRow(10, 0, true)]
	public void ContainsHandlesMidnightWrap(int hour, int minute, bool expected) {
		Assert.AreEqual(expected, Utc("10:00-01:00").Contains(At(2026, 1, 1, hour, minute)));
	}

	[TestMethod]
	public void FullDayWindowAlwaysContains() => Assert.IsTrue(Utc("00:00-00:00").Contains(At(2026, 1, 1, 4, 0)));

	[TestMethod]
	public void ContainsUsesTimeZone() {
		ActiveWindow window = ActiveWindow.FromConfig(new DriftConfig { ActiveHours = "10:00-11:00", TimeZone = "America/Bogota" });

		Assert.IsTrue(window.Contains(At(2026, 1, 1, 15, 30)));
		Assert.IsFalse(window.Contains(At(2026, 1, 1, 10, 30)));
	}

	[TestMethod]
	public void ContainsHandlesDaylightSaving() {
		ActiveWindow window = ActiveWindow.FromConfig(new DriftConfig { ActiveHours = "10:00-11:00", TimeZone = "America/New_York" });

		Assert.IsTrue(window.Contains(At(2026, 7, 1, 14, 30)));
		Assert.IsTrue(window.Contains(At(2026, 1, 15, 15, 30)));
		Assert.IsFalse(window.Contains(At(2026, 1, 15, 14, 30)));
	}

	[TestMethod]
	public void EstimateSkipsTimeOutsideWindow() {
		DateTime? eta = Utc("10:00-11:00").EstimateUtc(At(2026, 1, 1, 10, 59), 2);

		Assert.AreEqual(At(2026, 1, 2, 10, 1), eta);
	}

	[TestMethod]
	public void EstimateWithZeroMinutesReturnsNow() {
		DateTime now = At(2026, 1, 1, 12, 0);

		Assert.AreEqual(now, Utc("10:00-11:00").EstimateUtc(now, 0));
	}
}
```

- [ ] **Step 2: Ejecutar**

Run: `dotnet test AchievementDrift.Tests --filter "FullyQualifiedName~ActiveWindowTests" --nologo`
Expected: PASS. Si algo falla, corregir `ActiveWindow.cs` (no el test) y repetir.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "test(core): cover ActiveWindow parsing, wrap, time zones and ETA"
```

---

### Task 4: Modelo del plan + `PlanGenerator`

**Files:**
- Create: `AchievementDrift/Core/AchievementInfo.cs`, `AchievementDrift/Core/GamePlan.cs`, `AchievementDrift/Core/PlanGenerator.cs`
- Test: `AchievementDrift.Tests/PlanGeneratorTests.cs`

**Interfaces:**
- Consumes: `DriftConfig` (Task 2).
- Produces:
  - `internal sealed record AchievementInfo(string ApiName, string DisplayName, double GlobalPercent)` — `GlobalPercent = double.NaN` si Steam no lo da.
  - `internal enum EEntryState : byte { Pending, Unlocked, Simulated, Skipped }`
  - `internal sealed class PlannedAchievement { string ApiName; string DisplayName; double GlobalPercent; long OffsetMinutes; EEntryState State; DateTime? DoneAtUtc }` (props `get; set;`)
  - `internal sealed class GamePlan { uint AppId; string GameName; DateTime CreatedAtUtc; bool CreatedInDryRun; long ActiveMinutes; List<PlannedAchievement> Entries; bool IsCompleted [JsonIgnore]; long PlanMinutes [JsonIgnore]; PlannedAchievement? GetNextPending() }`
  - `internal sealed class BotState { Dictionary<uint, GamePlan> Plans; List<uint> ActiveGames }`
  - `PlanGenerator.MinGapMinutes = 15`, `PlanGenerator.DefaultPercent = 50`, `PlanGenerator.MinPercent = 0.1`
  - `static GamePlan PlanGenerator.Create(uint appId, string gameName, IReadOnlyList<AchievementInfo> pending, int totalAchievements, DriftConfig config, int windowMinutesPerDay, Random random, DateTime nowUtc)`
  - `static void PlanGenerator.Append(GamePlan plan, IReadOnlyList<AchievementInfo> newOnes, int totalAchievements, DriftConfig config, int windowMinutesPerDay, Random random)`
  - `static long PlanGenerator.ComputeBudget(int pendingCount, int totalCount, DriftConfig config, int windowMinutesPerDay, Random random)`
  - `static double PlanGenerator.NormalizePercent(double percent)`

- [ ] **Step 1: Escribir los tests**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using AchievementDrift.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class PlanGeneratorTests {
	private const int Window = 900;
	private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
	private static readonly DriftConfig Fixed30 = new() { MinDays = 30, MaxDays = 30 };

	private static List<AchievementInfo> Items(params double[] percents) => percents.Select((p, i) => new AchievementInfo($"ACH_{i}", $"Logro {i}", p)).ToList();

	[TestMethod]
	public void NeverFinishesBeforeMinDays() {
		for (int seed = 0; seed < 200; seed++) {
			List<AchievementInfo> items = Items(90, 70, 50, 20, 5, 1);
			GamePlan plan = PlanGenerator.Create(1, "Juego", items, items.Count, Fixed30, Window, new Random(seed), Now);

			Assert.IsTrue(plan.PlanMinutes >= 30L * Window, $"seed {seed}: {plan.PlanMinutes}");
		}
	}

	[TestMethod]
	public void TotalStaysCloseToBudget() {
		List<AchievementInfo> items = Items(90, 70, 50, 20, 5, 1);
		GamePlan plan = PlanGenerator.Create(1, "Juego", items, items.Count, Fixed30, Window, new Random(7), Now);

		long budget = 30L * Window;
		Assert.IsTrue(plan.PlanMinutes <= budget + (items.Count * (PlanGenerator.MinGapMinutes + 1)));
	}

	[TestMethod]
	public void GapsRespectMinimum() {
		List<AchievementInfo> items = Items(Enumerable.Repeat(99.0, 200).ToArray());
		GamePlan plan = PlanGenerator.Create(1, "Juego", items, items.Count, new DriftConfig { MinDays = 1, MaxDays = 1 }, 60, new Random(3), Now);

		long previous = 0;

		foreach (PlannedAchievement entry in plan.Entries.OrderBy(static e => e.OffsetMinutes)) {
			Assert.IsTrue(entry.OffsetMinutes - previous >= PlanGenerator.MinGapMinutes);
			previous = entry.OffsetMinutes;
		}
	}

	[TestMethod]
	public void RareAchievementsTendToComeLast() {
		int rareLast = 0;

		for (int seed = 0; seed < 1000; seed++) {
			GamePlan plan = PlanGenerator.Create(1, "Juego", [new AchievementInfo("COMMON", "Común", 80), new AchievementInfo("RARE", "Raro", 2)], 2, Fixed30, Window, new Random(seed), Now);

			long common = plan.Entries.Single(static e => e.ApiName == "COMMON").OffsetMinutes;
			long rare = plan.Entries.Single(static e => e.ApiName == "RARE").OffsetMinutes;

			if (rare > common) {
				rareLast++;
			}
		}

		Assert.IsTrue(rareLast > 900, $"rareLast = {rareLast}");
	}

	[TestMethod]
	public void RareAchievementsWaitLonger() {
		double commonGaps = 0;
		double rareGaps = 0;

		for (int seed = 0; seed < 500; seed++) {
			GamePlan plan = PlanGenerator.Create(1, "Juego", [new AchievementInfo("COMMON", "Común", 80), new AchievementInfo("RARE", "Raro", 2)], 2, Fixed30, Window, new Random(seed), Now);
			List<PlannedAchievement> ordered = plan.Entries.OrderBy(static e => e.OffsetMinutes).ToList();
			long first = ordered[0].OffsetMinutes;
			long second = ordered[1].OffsetMinutes - ordered[0].OffsetMinutes;

			if (ordered[0].ApiName == "RARE") {
				rareGaps += first;
				commonGaps += second;
			} else {
				commonGaps += first;
				rareGaps += second;
			}
		}

		Assert.IsTrue(rareGaps > commonGaps * 3, $"rare {rareGaps} vs common {commonGaps}");
	}

	[TestMethod]
	public void BudgetIsProportionalToPendingAchievements() {
		DriftConfig config = new() { MinDays = 30, MaxDays = 30 };

		Assert.AreEqual(30L * Window / 10, PlanGenerator.ComputeBudget(5, 50, config, Window, new Random(1)));
		Assert.AreEqual(30L * Window, PlanGenerator.ComputeBudget(50, 50, config, Window, new Random(1)));
	}

	[TestMethod]
	public void BudgetStaysWithinDayRange() {
		DriftConfig config = new() { MinDays = 30, MaxDays = 45 };

		for (int seed = 0; seed < 100; seed++) {
			long budget = PlanGenerator.ComputeBudget(10, 10, config, Window, new Random(seed));

			Assert.IsTrue(budget >= 30L * Window && budget <= 45L * Window);
		}
	}

	[TestMethod]
	public void PercentsAreNormalized() {
		Assert.AreEqual(PlanGenerator.DefaultPercent, PlanGenerator.NormalizePercent(double.NaN));
		Assert.AreEqual(PlanGenerator.MinPercent, PlanGenerator.NormalizePercent(0));
		Assert.AreEqual(100, PlanGenerator.NormalizePercent(250));
		Assert.AreEqual(12.5, PlanGenerator.NormalizePercent(12.5));

		GamePlan plan = PlanGenerator.Create(1, "Juego", Items(double.NaN), 1, Fixed30, Window, new Random(1), Now);
		Assert.AreEqual(PlanGenerator.DefaultPercent, plan.Entries[0].GlobalPercent);
	}

	[TestMethod]
	public void EmptyPendingProducesCompletedPlan() {
		GamePlan plan = PlanGenerator.Create(1, "Juego", [], 10, Fixed30, Window, new Random(1), Now);

		Assert.AreEqual(0, plan.Entries.Count);
		Assert.IsTrue(plan.IsCompleted);
	}

	[TestMethod]
	public void CreateFillsMetadata() {
		GamePlan plan = PlanGenerator.Create(440, "Team Fortress 2", Items(50), 1, Fixed30 with { DryRun = true }, Window, new Random(1), Now);

		Assert.AreEqual(440u, plan.AppId);
		Assert.AreEqual("Team Fortress 2", plan.GameName);
		Assert.AreEqual(Now, plan.CreatedAtUtc);
		Assert.IsTrue(plan.CreatedInDryRun);
		Assert.IsTrue(plan.Entries.TrueForAll(static e => e.State == EEntryState.Pending));
	}

	[TestMethod]
	public void AppendSchedulesAfterExistingPlan() {
		GamePlan plan = PlanGenerator.Create(1, "Juego", Items(50, 40), 2, Fixed30, Window, new Random(1), Now);
		long previousEnd = plan.PlanMinutes;

		PlanGenerator.Append(plan, Items(30), 3, Fixed30, Window, new Random(2));

		Assert.AreEqual(3, plan.Entries.Count);
		Assert.IsTrue(plan.Entries[2].OffsetMinutes > previousEnd);
	}

	[TestMethod]
	public void AppendStartsFromActiveMinutesWhenAhead() {
		GamePlan plan = PlanGenerator.Create(1, "Juego", Items(50), 1, Fixed30, Window, new Random(1), Now);
		plan.ActiveMinutes = plan.PlanMinutes + 1000;

		PlanGenerator.Append(plan, Items(30), 2, Fixed30, Window, new Random(2));

		Assert.IsTrue(plan.Entries[1].OffsetMinutes > plan.ActiveMinutes);
	}

	[TestMethod]
	public void GetNextPendingReturnsLowestOffset() {
		GamePlan plan = PlanGenerator.Create(1, "Juego", Items(50, 40, 30), 3, Fixed30, Window, new Random(1), Now);
		PlannedAchievement first = plan.Entries.MinBy(static e => e.OffsetMinutes)!;
		first.State = EEntryState.Unlocked;

		PlannedAchievement? next = plan.GetNextPending();

		Assert.IsNotNull(next);
		Assert.AreEqual(plan.Entries.Where(static e => e.State == EEntryState.Pending).Min(static e => e.OffsetMinutes), next.OffsetMinutes);
	}
}
```

- [ ] **Step 2: Ejecutar y ver que falla**

Run: `dotnet test AchievementDrift.Tests --filter "FullyQualifiedName~PlanGeneratorTests" --nologo`
Expected: error de compilación (`PlanGenerator` no existe).

- [ ] **Step 3: Implementar**

`AchievementDrift/Core/AchievementInfo.cs`:

```csharp
namespace AchievementDrift.Core;

// GlobalPercent = double.NaN cuando Steam no informa el porcentaje
internal sealed record AchievementInfo(string ApiName, string DisplayName, double GlobalPercent);
```

`AchievementDrift/Core/GamePlan.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace AchievementDrift.Core;

internal enum EEntryState : byte {
	Pending,
	Unlocked,
	Simulated,
	Skipped
}

internal sealed class PlannedAchievement {
	public string ApiName { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public double GlobalPercent { get; set; }
	public long OffsetMinutes { get; set; }
	public EEntryState State { get; set; }
	public DateTime? DoneAtUtc { get; set; }
}

internal sealed class GamePlan {
	public uint AppId { get; set; }
	public string GameName { get; set; } = "";
	public DateTime CreatedAtUtc { get; set; }
	public bool CreatedInDryRun { get; set; }
	public long ActiveMinutes { get; set; }
	public List<PlannedAchievement> Entries { get; set; } = [];

	[JsonIgnore]
	public bool IsCompleted => Entries.TrueForAll(static entry => entry.State != EEntryState.Pending);

	[JsonIgnore]
	public long PlanMinutes => Entries.Count == 0 ? 0 : Entries.Max(static entry => entry.OffsetMinutes);

	public PlannedAchievement? GetNextPending() => Entries.Where(static entry => entry.State == EEntryState.Pending).MinBy(static entry => entry.OffsetMinutes);
}

internal sealed class BotState {
	public Dictionary<uint, GamePlan> Plans { get; set; } = [];
	public List<uint> ActiveGames { get; set; } = [];
}
```

`AchievementDrift/Core/PlanGenerator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace AchievementDrift.Core;

internal static class PlanGenerator {
	internal const double DefaultPercent = 50;
	internal const int MinGapMinutes = 15;
	internal const double MinPercent = 0.1;

	internal static void Append(GamePlan plan, IReadOnlyList<AchievementInfo> newOnes, int totalAchievements, DriftConfig config, int windowMinutesPerDay, Random random) {
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(newOnes);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(random);

		if (newOnes.Count == 0) {
			return;
		}

		long start = Math.Max(plan.PlanMinutes, plan.ActiveMinutes);
		long budget = ComputeBudget(newOnes.Count, totalAchievements, config, windowMinutesPerDay, random);

		plan.Entries.AddRange(Schedule(newOnes, budget, start, random));
	}

	internal static long ComputeBudget(int pendingCount, int totalCount, DriftConfig config, int windowMinutesPerDay, Random random) {
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(random);

		double days = config.MinDays + (random.NextDouble() * (config.MaxDays - config.MinDays));
		double minutes = days * windowMinutesPerDay;

		if (totalCount > 0) {
			// Multiplicar antes de dividir evita errores de redondeo (27000 * 5 / 50 == 2700 exacto)
			minutes = minutes * Math.Clamp(pendingCount, 0, totalCount) / totalCount;
		}

		return (long) Math.Ceiling(minutes);
	}

	internal static GamePlan Create(uint appId, string gameName, IReadOnlyList<AchievementInfo> pending, int totalAchievements, DriftConfig config, int windowMinutesPerDay, Random random, DateTime nowUtc) {
		ArgumentNullException.ThrowIfNull(gameName);
		ArgumentNullException.ThrowIfNull(pending);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(random);

		GamePlan plan = new() {
			AppId = appId,
			GameName = gameName,
			CreatedAtUtc = nowUtc,
			CreatedInDryRun = config.DryRun
		};

		if (pending.Count > 0) {
			long budget = ComputeBudget(pending.Count, totalAchievements, config, windowMinutesPerDay, random);

			plan.Entries.AddRange(Schedule(pending, budget, 0, random));
		}

		return plan;
	}

	internal static double NormalizePercent(double percent) => double.IsNaN(percent) ? DefaultPercent : Math.Clamp(percent, MinPercent, 100);

	private static List<PlannedAchievement> Schedule(IReadOnlyList<AchievementInfo> items, long budgetMinutes, long startOffset, Random random) {
		// Orden aleatorio ponderado (Efraimidis-Spirakis): clave U^(1/p), mayor primero. Los comunes tienden a salir antes.
		List<(AchievementInfo Item, double Percent)> ordered = items
			.Select(item => (Item: item, Percent: NormalizePercent(item.GlobalPercent), Key: Math.Pow(1 - random.NextDouble(), 1 / NormalizePercent(item.GlobalPercent))))
			.OrderByDescending(static entry => entry.Key)
			.Select(static entry => (entry.Item, entry.Percent))
			.ToList();

		// Hueco previo proporcional a 1/sqrt(p) con jitter, escalado para sumar el presupuesto
		double[] gaps = ordered.Select(entry => (1 / Math.Sqrt(entry.Percent)) * (0.5 + random.NextDouble())).ToArray();
		double scale = budgetMinutes / gaps.Sum();

		List<PlannedAchievement> result = new(ordered.Count);
		long offset = startOffset;

		for (int i = 0; i < ordered.Count; i++) {
			offset += Math.Max(MinGapMinutes, (long) Math.Ceiling(gaps[i] * scale));

			result.Add(
				new PlannedAchievement {
					ApiName = ordered[i].Item.ApiName,
					DisplayName = ordered[i].Item.DisplayName,
					GlobalPercent = ordered[i].Percent,
					OffsetMinutes = offset,
					State = EEntryState.Pending
				}
			);
		}

		return result;
	}
}
```

- [ ] **Step 4: Ejecutar tests**

Run: `dotnet test AchievementDrift.Tests --filter "FullyQualifiedName~PlanGeneratorTests" --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add plan model and randomized rarity-weighted PlanGenerator"
```

---

### Task 5: `DriftEngine` (decisión de cada tick)

**Files:**
- Create: `AchievementDrift/Core/DriftEngine.cs`
- Test: `AchievementDrift.Tests/DriftEngineTests.cs`

**Interfaces:**
- Consumes: `BotState`, `GamePlan`, `PlannedAchievement`, `EEntryState` (Task 4); `DriftConfig` (Task 2); `ActiveWindow` (Task 2/3).
- Produces:
  - `internal sealed record UnlockDecision(uint AppId, PlannedAchievement Entry)`
  - `internal sealed record TickResult(IReadOnlyList<uint> GamesNeedingPlan, UnlockDecision? Unlock, bool StateChanged)` con `static TickResult Idle`.
  - `internal sealed class DriftEngine(Random random)` con `TickResult Tick(BotState state, DateTime nowUtc, IReadOnlyCollection<uint> eligiblePlaying, DriftConfig config, ActiveWindow window)`.
  - `static void DriftEngine.ApplyResult(PlannedAchievement entry, EEntryState state, DateTime nowUtc)`.

- [ ] **Step 1: Escribir los tests**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using AchievementDrift.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class DriftEngineTests {
	private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
	private static readonly ActiveWindow AlwaysOn = new(new TimeOnly(0, 0), new TimeOnly(0, 0), TimeZoneInfo.Utc);
	private static readonly ActiveWindow NeverNow = new(new TimeOnly(1, 0), new TimeOnly(2, 0), TimeZoneInfo.Utc);
	private static readonly DriftConfig Config = new() { DryRun = false, MaxActiveGames = 2 };

	private static GamePlan Plan(uint appId, long activeMinutes, params long[] offsets) => new() {
		AppId = appId,
		GameName = $"Juego {appId}",
		ActiveMinutes = activeMinutes,
		Entries = offsets.Select((offset, i) => new PlannedAchievement { ApiName = $"A{appId}_{i}", DisplayName = $"Logro {i}", GlobalPercent = 50, OffsetMinutes = offset }).ToList()
	};

	private static DriftEngine Engine() => new(new Random(42));

	[TestMethod]
	public void OutsideWindowDoesNothing() {
		BotState state = new() { Plans = { [10] = Plan(10, 0, 100) }, ActiveGames = { 10 } };

		TickResult result = Engine().Tick(state, Now, [10], Config, NeverNow);

		Assert.AreEqual(0, state.Plans[10].ActiveMinutes);
		Assert.IsNull(result.Unlock);
	}

	[TestMethod]
	public void DisabledDoesNothing() {
		BotState state = new() { Plans = { [10] = Plan(10, 0, 100) }, ActiveGames = { 10 } };

		Engine().Tick(state, Now, [10], Config with { Enabled = false }, AlwaysOn);

		Assert.AreEqual(0, state.Plans[10].ActiveMinutes);
	}

	[TestMethod]
	public void ActiveGameAccumulatesMinutes() {
		BotState state = new() { Plans = { [10] = Plan(10, 5, 100) }, ActiveGames = { 10 } };

		TickResult result = Engine().Tick(state, Now, [10], Config, AlwaysOn);

		Assert.AreEqual(6, state.Plans[10].ActiveMinutes);
		Assert.IsTrue(result.StateChanged);
	}

	[TestMethod]
	public void StoppedGameIsPausedAndFreesSlot() {
		BotState state = new() { Plans = { [10] = Plan(10, 5, 100) }, ActiveGames = { 10 } };

		Engine().Tick(state, Now, [], Config, AlwaysOn);

		Assert.AreEqual(5, state.Plans[10].ActiveMinutes);
		Assert.AreEqual(0, state.ActiveGames.Count);
		Assert.IsTrue(state.Plans.ContainsKey(10));
	}

	[TestMethod]
	public void RespectsMaxActiveGames() {
		BotState state = new();

		TickResult result = Engine().Tick(state, Now, [1, 2, 3, 4, 5], Config, AlwaysOn);

		Assert.AreEqual(2, state.ActiveGames.Count);
		CollectionAssert.AreEquivalent(state.ActiveGames, result.GamesNeedingPlan.ToList());
	}

	[TestMethod]
	public void PrefersStartedPlansWhenFillingSlots() {
		BotState state = new() { Plans = { [3] = Plan(3, 50, 100), [5] = Plan(5, 20, 100) } };

		Engine().Tick(state, Now, [1, 2, 3, 4, 5], Config, AlwaysOn);

		CollectionAssert.AreEquivalent(new List<uint> { 3, 5 }, state.ActiveGames);
	}

	[TestMethod]
	public void ShrinksWhenMaxActiveGamesIsReduced() {
		BotState state = new() { Plans = { [1] = Plan(1, 0, 100), [2] = Plan(2, 0, 100) }, ActiveGames = { 1, 2 } };

		Engine().Tick(state, Now, [1, 2], Config with { MaxActiveGames = 1 }, AlwaysOn);

		Assert.AreEqual(1, state.ActiveGames.Count);
	}

	[TestMethod]
	public void CompletedGamesNeverTakeSlots() {
		GamePlan done = Plan(1, 500, 100);
		done.Entries[0].State = EEntryState.Unlocked;
		BotState state = new() { Plans = { [1] = done }, ActiveGames = { 1 } };

		Engine().Tick(state, Now, [1], Config, AlwaysOn);

		Assert.AreEqual(0, state.ActiveGames.Count);
	}

	[TestMethod]
	public void ReturnsAtMostOneDueUnlockWithLowestOffset() {
		BotState state = new() { Plans = { [1] = Plan(1, 1000, 300, 200), [2] = Plan(2, 1000, 250) }, ActiveGames = { 1, 2 } };

		TickResult result = Engine().Tick(state, Now, [1, 2], Config, AlwaysOn);

		Assert.IsNotNull(result.Unlock);
		Assert.AreEqual(1u, result.Unlock.AppId);
		Assert.AreEqual(200, result.Unlock.Entry.OffsetMinutes);
	}

	[TestMethod]
	public void NotDueYetReturnsNoUnlock() {
		BotState state = new() { Plans = { [1] = Plan(1, 10, 100) }, ActiveGames = { 1 } };

		TickResult result = Engine().Tick(state, Now, [1], Config, AlwaysOn);

		Assert.IsNull(result.Unlock);
	}

	[TestMethod]
	public void DryRunPlansAreDroppedWhenSwitchingToRealMode() {
		GamePlan simulated = Plan(1, 10, 100);
		simulated.CreatedInDryRun = true;
		BotState state = new() { Plans = { [1] = simulated }, ActiveGames = { 1 } };

		TickResult result = Engine().Tick(state, Now, [1], Config with { DryRun = false }, AlwaysOn);

		Assert.IsFalse(state.Plans.ContainsKey(1));
		CollectionAssert.Contains(result.GamesNeedingPlan.ToList(), 1u);
	}

	[TestMethod]
	public void ApplyResultSetsStateAndTime() {
		PlannedAchievement entry = new();

		DriftEngine.ApplyResult(entry, EEntryState.Unlocked, Now);

		Assert.AreEqual(EEntryState.Unlocked, entry.State);
		Assert.AreEqual(Now, entry.DoneAtUtc);
	}
}
```

- [ ] **Step 2: Ejecutar y ver que falla**

Run: `dotnet test AchievementDrift.Tests --filter "FullyQualifiedName~DriftEngineTests" --nologo`
Expected: error de compilación (`DriftEngine` no existe).

- [ ] **Step 3: Implementar `AchievementDrift/Core/DriftEngine.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace AchievementDrift.Core;

internal sealed record UnlockDecision(uint AppId, PlannedAchievement Entry);

internal sealed record TickResult(IReadOnlyList<uint> GamesNeedingPlan, UnlockDecision? Unlock, bool StateChanged) {
	internal static readonly TickResult Idle = new([], null, false);
}

internal sealed class DriftEngine(Random random) {
	private readonly Random Random = random ?? throw new ArgumentNullException(nameof(random));

	internal static void ApplyResult(PlannedAchievement entry, EEntryState state, DateTime nowUtc) {
		ArgumentNullException.ThrowIfNull(entry);

		entry.State = state;
		entry.DoneAtUtc = nowUtc;
	}

	internal TickResult Tick(BotState state, DateTime nowUtc, IReadOnlyCollection<uint> eligiblePlaying, DriftConfig config, ActiveWindow window) {
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(eligiblePlaying);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(window);

		bool changed = DropDryRunPlans(state, config);

		if (!config.Enabled || !window.Contains(nowUtc)) {
			return changed ? new TickResult([], null, true) : TickResult.Idle;
		}

		HashSet<uint> playing = [.. eligiblePlaying];

		changed |= state.ActiveGames.RemoveAll(appId => !playing.Contains(appId) || (state.Plans.TryGetValue(appId, out GamePlan? plan) && plan.IsCompleted)) > 0;

		if (state.ActiveGames.Count > config.MaxActiveGames) {
			state.ActiveGames.RemoveRange(config.MaxActiveGames, state.ActiveGames.Count - config.MaxActiveGames);
			changed = true;
		}

		changed |= FillSlots(state, playing, config.MaxActiveGames);

		List<uint> needPlan = [];

		foreach (uint appId in state.ActiveGames) {
			if (state.Plans.TryGetValue(appId, out GamePlan? plan)) {
				plan.ActiveMinutes++;
				changed = true;
			} else {
				needPlan.Add(appId);
			}
		}

		return new TickResult(needPlan, FindDue(state), changed);
	}

	private static bool DropDryRunPlans(BotState state, DriftConfig config) {
		if (config.DryRun) {
			return false;
		}

		List<uint> simulated = state.Plans.Values.Where(static plan => plan.CreatedInDryRun).Select(static plan => plan.AppId).ToList();

		foreach (uint appId in simulated) {
			state.Plans.Remove(appId);
			state.ActiveGames.Remove(appId);
		}

		return simulated.Count > 0;
	}

	private static UnlockDecision? FindDue(BotState state) => state.ActiveGames
		.Where(state.Plans.ContainsKey)
		.Select(appId => state.Plans[appId])
		.SelectMany(static plan => plan.Entries.Where(entry => (entry.State == EEntryState.Pending) && (entry.OffsetMinutes <= plan.ActiveMinutes)).Select(entry => new UnlockDecision(plan.AppId, entry)))
		.MinBy(static decision => decision.Entry.OffsetMinutes);

	private bool FillSlots(BotState state, HashSet<uint> playing, int maxActiveGames) {
		int free = maxActiveGames - state.ActiveGames.Count;

		if (free <= 0) {
			return false;
		}

		List<uint> candidates = playing.Where(appId => !state.ActiveGames.Contains(appId) && !(state.Plans.TryGetValue(appId, out GamePlan? plan) && plan.IsCompleted)).ToList();

		// Primero se retoman juegos empezados, luego juegos nuevos; ambos en orden aleatorio
		IEnumerable<uint> started = candidates.Where(state.Plans.ContainsKey).OrderBy(_ => Random.Next());
		IEnumerable<uint> fresh = candidates.Where(appId => !state.Plans.ContainsKey(appId)).OrderBy(_ => Random.Next());

		List<uint> chosen = started.Concat(fresh).Take(free).ToList();
		state.ActiveGames.AddRange(chosen);

		return chosen.Count > 0;
	}
}
```

- [ ] **Step 4: Ejecutar tests**

Run: `dotnet test AchievementDrift.Tests --filter "FullyQualifiedName~DriftEngineTests" --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add DriftEngine tick logic with active-game slots"
```

---

### Task 6: `StoreInfo` + `SafetyRules`

**Files:**
- Create: `AchievementDrift/Core/StoreInfo.cs`, `AchievementDrift/Core/SafetyRules.cs`
- Test: `AchievementDrift.Tests/SafetyRulesTests.cs`

**Interfaces:**
- Consumes: `DriftConfig`.
- Produces:
  - `internal sealed record StoreInfo(bool Found, string Name, ImmutableHashSet<int> Categories)`
  - `internal enum EEligibility : byte { Eligible, Unknown, Blacklisted, Vac, Online, NoAchievements }`
  - `static EEligibility SafetyRules.Evaluate(uint appId, DriftConfig config, StoreInfo? info)`; `static bool SafetyRules.NeedsStoreInfo(uint appId, DriftConfig config)`; `static string SafetyRules.Describe(EEligibility)`.
  - Constantes `SafetyRules.VacCategory = 8`, `SafetyRules.AchievementsCategory = 22`, `SafetyRules.OnlineCategories = {1,20,27,36,38,49}`.

- [ ] **Step 1: Escribir los tests**

```csharp
using AchievementDrift.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class SafetyRulesTests {
	private static StoreInfo Store(params int[] categories) => new(true, "Juego", [.. categories]);

	[TestMethod]
	public void BlacklistWinsOverAllowlist() {
		DriftConfig config = new() { Blacklist = [10], Allowlist = [10] };

		Assert.AreEqual(EEligibility.Blacklisted, SafetyRules.Evaluate(10, config, Store(22)));
	}

	[TestMethod]
	public void AllowlistBypassesStoreChecks() {
		DriftConfig config = new() { Allowlist = [10] };

		Assert.AreEqual(EEligibility.Eligible, SafetyRules.Evaluate(10, config, null));
		Assert.AreEqual(EEligibility.Eligible, SafetyRules.Evaluate(10, config, Store(8, 1, 22)));
		Assert.IsFalse(SafetyRules.NeedsStoreInfo(10, config));
	}

	[TestMethod]
	public void MissingOrUnknownStoreInfoIsUnknown() {
		DriftConfig config = new();

		Assert.AreEqual(EEligibility.Unknown, SafetyRules.Evaluate(10, config, null));
		Assert.AreEqual(EEligibility.Unknown, SafetyRules.Evaluate(10, config, new StoreInfo(false, "", [])));
		Assert.IsTrue(SafetyRules.NeedsStoreInfo(10, config));
	}

	[TestMethod]
	public void VacIsExcluded() {
		Assert.AreEqual(EEligibility.Vac, SafetyRules.Evaluate(10, new DriftConfig(), Store(8, 22)));
		Assert.AreEqual(EEligibility.Eligible, SafetyRules.Evaluate(10, new DriftConfig { ExcludeVac = false }, Store(8, 22)));
	}

	[TestMethod]
	[DataRow(1)]
	[DataRow(20)]
	[DataRow(27)]
	[DataRow(36)]
	[DataRow(38)]
	[DataRow(49)]
	public void OnlineIsExcluded(int category) {
		Assert.AreEqual(EEligibility.Online, SafetyRules.Evaluate(10, new DriftConfig(), Store(category, 22)));
		Assert.AreEqual(EEligibility.Eligible, SafetyRules.Evaluate(10, new DriftConfig { ExcludeOnline = false }, Store(category, 22)));
	}

	[TestMethod]
	public void GamesWithoutAchievementsAreExcluded() {
		Assert.AreEqual(EEligibility.NoAchievements, SafetyRules.Evaluate(10, new DriftConfig(), Store(2)));
	}

	[TestMethod]
	public void SinglePlayerWithAchievementsIsEligible() {
		Assert.AreEqual(EEligibility.Eligible, SafetyRules.Evaluate(10, new DriftConfig(), Store(2, 22)));
	}
}
```

- [ ] **Step 2: Ejecutar y ver que falla**

Run: `dotnet test AchievementDrift.Tests --filter "FullyQualifiedName~SafetyRulesTests" --nologo`
Expected: error de compilación.

- [ ] **Step 3: Implementar**

`AchievementDrift/Core/StoreInfo.cs`:

```csharp
using System.Collections.Immutable;

namespace AchievementDrift.Core;

// Found = false cuando la tienda no conoce el appid (retirado, región, etc.)
internal sealed record StoreInfo(bool Found, string Name, ImmutableHashSet<int> Categories);
```

`AchievementDrift/Core/SafetyRules.cs`:

```csharp
using System;
using System.Collections.Immutable;
using System.Linq;

namespace AchievementDrift.Core;

internal enum EEligibility : byte {
	Eligible,
	Unknown,
	Blacklisted,
	Vac,
	Online,
	NoAchievements
}

internal static class SafetyRules {
	internal const int AchievementsCategory = 22;
	internal const int VacCategory = 8;

	// Multi-player, MMO, Cross-Platform Multiplayer, Online PvP, Online Co-op, PvP
	internal static readonly ImmutableHashSet<int> OnlineCategories = [1, 20, 27, 36, 38, 49];

	internal static string Describe(EEligibility eligibility) => eligibility switch {
		EEligibility.Eligible => "elegible",
		EEligibility.Unknown => "sin datos de tienda (añádelo a Allowlist si es seguro)",
		EEligibility.Blacklisted => "en Blacklist",
		EEligibility.Vac => "tiene VAC",
		EEligibility.Online => "es online/multijugador",
		EEligibility.NoAchievements => "no tiene logros",
		_ => throw new ArgumentOutOfRangeException(nameof(eligibility))
	};

	internal static EEligibility Evaluate(uint appId, DriftConfig config, StoreInfo? info) {
		ArgumentNullException.ThrowIfNull(config);

		if (config.Blacklist.Contains(appId)) {
			return EEligibility.Blacklisted;
		}

		if (config.Allowlist.Contains(appId)) {
			return EEligibility.Eligible;
		}

		if (info is not { Found: true }) {
			return EEligibility.Unknown;
		}

		if (config.ExcludeVac && info.Categories.Contains(VacCategory)) {
			return EEligibility.Vac;
		}

		if (config.ExcludeOnline && info.Categories.Any(OnlineCategories.Contains)) {
			return EEligibility.Online;
		}

		return info.Categories.Contains(AchievementsCategory) ? EEligibility.Eligible : EEligibility.NoAchievements;
	}

	internal static bool NeedsStoreInfo(uint appId, DriftConfig config) {
		ArgumentNullException.ThrowIfNull(config);

		return !config.Blacklist.Contains(appId) && !config.Allowlist.Contains(appId);
	}
}
```

- [ ] **Step 4: Ejecutar tests**

Run: `dotnet test AchievementDrift.Tests --filter "FullyQualifiedName~SafetyRulesTests" --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add SafetyRules (blacklist, allowlist, VAC, online, no achievements)"
```

---

### Task 7: DTOs de estado + `StatusBuilder`

**Files:**
- Create: `AchievementDrift/Core/DriftStatus.cs`, `AchievementDrift/Core/StatusBuilder.cs`
- Test: `AchievementDrift.Tests/StatusBuilderTests.cs`

**Interfaces:**
- Consumes: `BotState`, `GamePlan`, `EEntryState`, `DriftConfig`, `ActiveWindow`.
- Produces:
  - `public sealed record DriftStatus(string BotName, bool Connected, bool Enabled, bool DryRun, bool InActiveWindow, string ActiveHours, string TimeZone, int MaxActiveGames, IReadOnlyList<GameStatus> Games)`
  - `public sealed record GameStatus(uint AppId, string Name, string State, int Done, int Skipped, int Total, double ProgressPercent, double PlayedHours, double PlanHours, int DaysSinceStart, NextAchievementStatus? Next)` — `State` ∈ `"Active" | "Paused" | "Completed"`.
  - `public sealed record NextAchievementStatus(string Name, double GlobalPercent, DateTime? EtaUtc)`
  - `static DriftStatus StatusBuilder.Build(string botName, bool connected, DriftConfig config, ActiveWindow window, BotState state, DateTime nowUtc)`
  - `static string StatusBuilder.FormatText(DriftStatus status)`
  - `static string StatusBuilder.FormatPlan(GamePlan plan)`

- [ ] **Step 1: Escribir los tests**

```csharp
using System;
using System.Linq;
using AchievementDrift.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class StatusBuilderTests {
	private static readonly DateTime Now = new(2026, 1, 11, 12, 0, 0, DateTimeKind.Utc);
	private static readonly ActiveWindow AlwaysOn = new(new TimeOnly(0, 0), new TimeOnly(0, 0), TimeZoneInfo.Utc);

	private static GamePlan Plan(uint appId, long activeMinutes, params (long Offset, EEntryState State)[] entries) => new() {
		AppId = appId,
		GameName = $"Juego {appId}",
		CreatedAtUtc = Now.AddDays(-10),
		ActiveMinutes = activeMinutes,
		Entries = entries.Select((e, i) => new PlannedAchievement { ApiName = $"A{i}", DisplayName = $"Logro {i}", GlobalPercent = 12.5, OffsetMinutes = e.Offset, State = e.State }).ToList()
	};

	private static BotState SampleState() => new() {
		Plans = {
			[1] = Plan(1, 60, (30, EEntryState.Unlocked), (120, EEntryState.Pending)),
			[2] = Plan(2, 10, (100, EEntryState.Pending)),
			[3] = Plan(3, 500, (50, EEntryState.Unlocked), (60, EEntryState.Skipped))
		},
		ActiveGames = { 1 }
	};

	[TestMethod]
	public void GamesAreOrderedActivePausedCompleted() {
		DriftStatus status = StatusBuilder.Build("bot", true, new DriftConfig(), AlwaysOn, SampleState(), Now);

		CollectionAssert.AreEqual(new[] { "Active", "Paused", "Completed" }, status.Games.Select(static g => g.State).ToArray());
	}

	[TestMethod]
	public void ActiveGameHasProgressAndEta() {
		GameStatus game = StatusBuilder.Build("bot", true, new DriftConfig(), AlwaysOn, SampleState(), Now).Games[0];

		Assert.AreEqual(1, game.Done);
		Assert.AreEqual(2, game.Total);
		Assert.AreEqual(50, game.ProgressPercent);
		Assert.AreEqual(1, game.PlayedHours);
		Assert.AreEqual(2, game.PlanHours);
		Assert.AreEqual(10, game.DaysSinceStart);
		Assert.IsNotNull(game.Next);
		Assert.AreEqual("Logro 1", game.Next.Name);
		Assert.AreEqual(Now.AddMinutes(60), game.Next.EtaUtc);
	}

	[TestMethod]
	public void PausedGameHasNoEta() {
		GameStatus game = StatusBuilder.Build("bot", true, new DriftConfig(), AlwaysOn, SampleState(), Now).Games[1];

		Assert.IsNotNull(game.Next);
		Assert.IsNull(game.Next.EtaUtc);
	}

	[TestMethod]
	public void CompletedGameCountsSkipped() {
		GameStatus game = StatusBuilder.Build("bot", true, new DriftConfig(), AlwaysOn, SampleState(), Now).Games[2];

		Assert.AreEqual(1, game.Done);
		Assert.AreEqual(1, game.Skipped);
		Assert.IsNull(game.Next);
		Assert.AreEqual(100, game.ProgressPercent);
	}

	[TestMethod]
	public void TextMentionsBotAndGames() {
		string text = StatusBuilder.FormatText(StatusBuilder.Build("manticora", true, new DriftConfig(), AlwaysOn, SampleState(), Now));

		StringAssert.Contains(text, "Juego 1");
		StringAssert.Contains(text, "DryRun");
	}

	[TestMethod]
	public void PlanTextListsEntries() {
		string text = StatusBuilder.FormatPlan(SampleState().Plans[1]);

		StringAssert.Contains(text, "Logro 0");
		StringAssert.Contains(text, "Logro 1");
	}
}
```

- [ ] **Step 2: Ejecutar y ver que falla**

Run: `dotnet test AchievementDrift.Tests --filter "FullyQualifiedName~StatusBuilderTests" --nologo`
Expected: error de compilación.

- [ ] **Step 3: Implementar**

`AchievementDrift/Core/DriftStatus.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace AchievementDrift.Core;

public sealed record DriftStatus(string BotName, bool Connected, bool Enabled, bool DryRun, bool InActiveWindow, string ActiveHours, string TimeZone, int MaxActiveGames, IReadOnlyList<GameStatus> Games);

public sealed record GameStatus(uint AppId, string Name, string State, int Done, int Skipped, int Total, double ProgressPercent, double PlayedHours, double PlanHours, int DaysSinceStart, NextAchievementStatus? Next);

public sealed record NextAchievementStatus(string Name, double GlobalPercent, DateTime? EtaUtc);
```

`AchievementDrift/Core/StatusBuilder.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AchievementDrift.Core;

internal static class StatusBuilder {
	internal const string StateActive = "Active";
	internal const string StateCompleted = "Completed";
	internal const string StatePaused = "Paused";

	internal static DriftStatus Build(string botName, bool connected, DriftConfig config, ActiveWindow window, BotState state, DateTime nowUtc) {
		ArgumentNullException.ThrowIfNull(botName);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(window);
		ArgumentNullException.ThrowIfNull(state);

		List<GameStatus> games = state.Plans.Values
			.Select(plan => BuildGame(plan, state.ActiveGames.Contains(plan.AppId), window, nowUtc))
			.OrderBy(static game => game.State switch { StateActive => 0, StatePaused => 1, _ => 2 })
			.ThenBy(static game => game.AppId)
			.ToList();

		return new DriftStatus(botName, connected, config.Enabled, config.DryRun, window.Contains(nowUtc), config.ActiveHours, config.DescribeTimeZone(), config.MaxActiveGames, games);
	}

	internal static string FormatPlan(GamePlan plan) {
		ArgumentNullException.ThrowIfNull(plan);

		StringBuilder text = new();
		text.AppendLine(CultureInfo.InvariantCulture, $"Plan de {plan.GameName} ({plan.AppId}) — {Hours(plan.ActiveMinutes)} h jugadas de {Hours(plan.PlanMinutes)} h:");

		foreach (PlannedAchievement entry in plan.Entries.OrderBy(static e => e.OffsetMinutes)) {
			string mark = entry.State switch {
				EEntryState.Unlocked => "✅",
				EEntryState.Simulated => "🧪",
				EEntryState.Skipped => "⛔",
				_ => "⏳"
			};

			text.AppendLine(CultureInfo.InvariantCulture, $"{mark} {entry.DisplayName} ({entry.GlobalPercent:0.#}%) — a las {Hours(entry.OffsetMinutes)} h");
		}

		return text.ToString().TrimEnd();
	}

	internal static string FormatText(DriftStatus status) {
		ArgumentNullException.ThrowIfNull(status);

		StringBuilder text = new();
		text.AppendLine(CultureInfo.InvariantCulture, $"AchievementDrift: {(status.Enabled ? "activado" : "desactivado")}{(status.DryRun ? " · DryRun (simulación)" : "")} · horario {status.ActiveHours} ({status.TimeZone}) · {(status.InActiveWindow ? "dentro" : "fuera")} del horario");

		if (status.Games.Count == 0) {
			text.Append("Todavía no hay planes. Se crean cuando el bot juega un juego elegible dentro del horario.");

			return text.ToString();
		}

		foreach (GameStatus game in status.Games) {
			string state = game.State switch {
				StateActive => "▶️",
				StatePaused => "⏸️",
				_ => "🏁"
			};

			text.Append(CultureInfo.InvariantCulture, $"{state} {game.Name}: {game.Done}/{game.Total} ({game.ProgressPercent:0}% del plan, día {game.DaysSinceStart})");

			if (game.Next != null) {
				text.Append(CultureInfo.InvariantCulture, $" · siguiente: {game.Next.Name} ({game.Next.GlobalPercent:0.#}%)");

				if (game.Next.EtaUtc is DateTime eta) {
					text.Append(CultureInfo.InvariantCulture, $" ~{eta:yyyy-MM-dd HH:mm} UTC");
				}
			}

			text.AppendLine();
		}

		return text.ToString().TrimEnd();
	}

	private static GameStatus BuildGame(GamePlan plan, bool isActive, ActiveWindow window, DateTime nowUtc) {
		string state = plan.IsCompleted ? StateCompleted : isActive ? StateActive : StatePaused;
		int done = plan.Entries.Count(static e => e.State is EEntryState.Unlocked or EEntryState.Simulated);
		int skipped = plan.Entries.Count(static e => e.State == EEntryState.Skipped);
		long planMinutes = plan.PlanMinutes;
		double progress = (planMinutes == 0) || plan.IsCompleted ? 100 : Math.Min(100, Math.Round(plan.ActiveMinutes * 100.0 / planMinutes, 1));

		PlannedAchievement? next = plan.GetNextPending();
		NextAchievementStatus? nextStatus = next == null ? null : new NextAchievementStatus(next.DisplayName, next.GlobalPercent, state == StateActive ? window.EstimateUtc(nowUtc, next.OffsetMinutes - plan.ActiveMinutes) : null);

		return new GameStatus(plan.AppId, plan.GameName, state, done, skipped, plan.Entries.Count, progress, Hours(plan.ActiveMinutes), Hours(planMinutes), (int) (nowUtc - plan.CreatedAtUtc).TotalDays, nextStatus);
	}

	private static double Hours(long minutes) => Math.Round(minutes / 60.0, 1);
}
```

- [ ] **Step 4: Ejecutar tests**

Run: `dotnet test AchievementDrift.Tests --filter "FullyQualifiedName~StatusBuilderTests" --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add status DTOs and text formatting"
```

---

### Task 8: `SchemaParser` (esquema de logros de Steam)

**Files:**
- Modify: `AchievementDrift/Steam/SchemaParser.cs` (reemplazar el stub)
- Test: `AchievementDrift.Tests/SchemaParserTests.cs`

**Interfaces:**
- Consumes: `SteamKit2.KeyValue`.
- Produces:
  - `internal sealed record SchemaAchievement(string ApiName, string DisplayName, uint StatId, int Bit, bool IsUnlocked, bool IsProtected, uint? DependencyStatId, uint DependencyValue)`
  - `static List<SchemaAchievement> SchemaParser.Parse(KeyValue schema, IReadOnlyDictionary<uint, uint> statValues)`

- [ ] **Step 1: Escribir los tests**

```csharp
using System.Collections.Generic;
using System.Linq;
using AchievementDrift.Steam;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamKit2;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class SchemaParserTests {
	private static KeyValue Node(string name, params KeyValue[] children) {
		KeyValue node = new(name);
		node.Children.AddRange(children);

		return node;
	}

	private static KeyValue Leaf(string name, string value) => new(name, value);

	private static KeyValue Bit(string bit, string apiName, KeyValue? displayNames = null, params KeyValue[] extra) {
		List<KeyValue> children = [Leaf("name", apiName), Node("display", displayNames ?? Node("name", Leaf("english", $"{apiName} EN")))];
		children.AddRange(extra);

		return Node(bit, [.. children]);
	}

	private static KeyValue Schema() => Node(
		"440",
		Node(
			"stats",
			Node(
				"1",
				Leaf("type", "4"),
				Node(
					"bits",
					Bit("0", "ACH_FIRST", Node("name", Leaf("english", "First"), Leaf("spanish", "Primero"))),
					Bit("1", "ACH_SECOND"),
					Bit("2", "ACH_SERVER", null, Leaf("permission", "2")),
					Bit("3", "ACH_PROGRESS", null, Node("progress", Node("value", Leaf("operation", "statvalue"), Leaf("operand1", "kills")), Leaf("max_val", "100")))
				)
			),
			Node("7", Leaf("type", "1"), Leaf("name", "kills")),
			Node(
				"2",
				Leaf("type", "ACHIEVEMENTS"),
				Node("bits", Bit("0", "ACH_PROTECTED_STAT", null, Node("progress", Node("value", Leaf("operand1", "secure_stat")), Leaf("max_val", "5"))))
			),
			Node("8", Leaf("type", "INT"), Leaf("name", "secure_stat"), Leaf("permission", "2"))
		)
	);

	private static List<SchemaAchievement> Parse(params (uint Stat, uint Value)[] values) => SchemaParser.Parse(Schema(), values.ToDictionary(static v => v.Stat, static v => v.Value));

	[TestMethod]
	public void ParsesAllAchievementBits() {
		CollectionAssert.AreEquivalent(new[] { "ACH_FIRST", "ACH_SECOND", "ACH_SERVER", "ACH_PROGRESS", "ACH_PROTECTED_STAT" }, Parse().Select(static a => a.ApiName).ToArray());
	}

	[TestMethod]
	public void DetectsUnlockedBits() {
		List<SchemaAchievement> result = Parse((1, 0b0010));

		Assert.IsFalse(result.Single(static a => a.ApiName == "ACH_FIRST").IsUnlocked);
		Assert.IsTrue(result.Single(static a => a.ApiName == "ACH_SECOND").IsUnlocked);
	}

	[TestMethod]
	public void PrefersSpanishDisplayName() {
		Assert.AreEqual("Primero", Parse().Single(static a => a.ApiName == "ACH_FIRST").DisplayName);
		Assert.AreEqual("ACH_SECOND EN", Parse().Single(static a => a.ApiName == "ACH_SECOND").DisplayName);
	}

	[TestMethod]
	public void PermissionMarksProtected() {
		Assert.IsTrue(Parse().Single(static a => a.ApiName == "ACH_SERVER").IsProtected);
		Assert.IsFalse(Parse().Single(static a => a.ApiName == "ACH_FIRST").IsProtected);
	}

	[TestMethod]
	public void ProgressDependencyIsResolved() {
		SchemaAchievement achievement = Parse().Single(static a => a.ApiName == "ACH_PROGRESS");

		Assert.AreEqual(7u, achievement.DependencyStatId);
		Assert.AreEqual(100u, achievement.DependencyValue);
		Assert.AreEqual(1u, achievement.StatId);
		Assert.AreEqual(3, achievement.Bit);
	}

	[TestMethod]
	public void ProtectedDependencyStatMarksAchievementProtected() {
		Assert.IsTrue(Parse().Single(static a => a.ApiName == "ACH_PROTECTED_STAT").IsProtected);
	}

	[TestMethod]
	public void EmptySchemaReturnsEmpty() {
		Assert.AreEqual(0, SchemaParser.Parse(new KeyValue("root"), new Dictionary<uint, uint>()).Count);
	}
}
```

- [ ] **Step 2: Ejecutar y ver que falla**

Run: `dotnet test AchievementDrift.Tests --filter "FullyQualifiedName~SchemaParserTests" --nologo`
Expected: error de compilación (`SchemaParser.Parse` no existe).

- [ ] **Step 3: Implementar `AchievementDrift/Steam/SchemaParser.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using SteamKit2;

namespace AchievementDrift.Steam;

internal sealed record SchemaAchievement(string ApiName, string DisplayName, uint StatId, int Bit, bool IsUnlocked, bool IsProtected, uint? DependencyStatId, uint DependencyValue);

// Interpreta el esquema KeyValues de ClientGetUserStatsResponse (ver spec §11)
internal static class SchemaParser {
	private static readonly string[] PreferredLanguages = ["spanish", "latam", "english"];

	internal static List<SchemaAchievement> Parse(KeyValue schema, IReadOnlyDictionary<uint, uint> statValues) {
		ArgumentNullException.ThrowIfNull(schema);
		ArgumentNullException.ThrowIfNull(statValues);

		KeyValue stats = schema["stats"];
		Dictionary<string, (uint Id, int Permission)> intStats = new(StringComparer.Ordinal);

		foreach (KeyValue stat in stats.Children) {
			string? type = stat["type"].Value?.ToUpperInvariant();
			string? name = stat["name"].Value;

			if (type is "1" or "INT" && TryParseUInt(stat.Name, out uint id) && !string.IsNullOrEmpty(name)) {
				intStats[name] = (id, ParseInt(stat["permission"].Value));
			}
		}

		List<SchemaAchievement> result = [];

		foreach (KeyValue stat in stats.Children) {
			string? type = stat["type"].Value?.ToUpperInvariant();

			if (type is not ("4" or "ACHIEVEMENTS") || !TryParseUInt(stat.Name, out uint statId)) {
				continue;
			}

			uint value = statValues.GetValueOrDefault(statId);

			foreach (KeyValue bitNode in stat["bits"].Children) {
				string? apiName = bitNode["name"].Value;

				if (string.IsNullOrEmpty(apiName) || !int.TryParse(bitNode.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int bit) || bit is < 0 or > 31) {
					continue;
				}

				bool isProtected = !string.IsNullOrEmpty(bitNode["permission"].Value);
				uint? dependencyStatId = null;
				uint dependencyValue = 0;

				string? operand = bitNode["progress"]["value"]["operand1"].Value;

				if (!string.IsNullOrEmpty(operand) && intStats.TryGetValue(operand, out (uint Id, int Permission) dependency)) {
					dependencyStatId = dependency.Id;
					_ = TryParseUInt(bitNode["progress"]["max_val"].Value, out dependencyValue);
					isProtected |= dependency.Permission > 1;
				}

				result.Add(new SchemaAchievement(apiName, GetDisplayName(bitNode, apiName), statId, bit, (value & (1u << bit)) != 0, isProtected, dependencyStatId, dependencyValue));
			}
		}

		return result;
	}

	private static string GetDisplayName(KeyValue bitNode, string fallback) {
		KeyValue names = bitNode["display"]["name"];

		foreach (string language in PreferredLanguages) {
			string? name = names[language].Value;

			if (!string.IsNullOrWhiteSpace(name)) {
				return name;
			}
		}

		return fallback;
	}

	private static int ParseInt(string? text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

	private static bool TryParseUInt(string? text, out uint value) => uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
```

- [ ] **Step 4: Ejecutar todos los tests**

Run: `dotnet test AchievementDrift.Tests --nologo`
Expected: PASS (todos los tests hasta ahora).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(steam): parse achievement schema (bits, protection, progress deps)"
```

---

### Task 9: Integración con Steam: `StatsHandler` + `WebApis`

**Files:**
- Create: `AchievementDrift/Steam/StatsHandler.cs`, `AchievementDrift/Steam/WebApis.cs`

**Interfaces:**
- Consumes: `SchemaParser`, `SchemaAchievement` (Task 8); `StoreInfo` (Task 6); ASF `Bot`, `WebBrowser`, `ObjectResponse<T>`, `Utilities.ToLongRunningTask`.
- Produces:
  - `internal sealed record StatsSnapshot(IReadOnlyList<SchemaAchievement> Achievements, IReadOnlyDictionary<uint, uint> StatValues, uint CrcStats)`
  - `internal sealed class StatsHandler(Bot bot) : ClientMsgHandler` con `Task<StatsSnapshot?> GetAsync(uint appId)` y `Task<bool?> UnlockAsync(uint appId, SchemaAchievement achievement, StatsSnapshot snapshot)` (`true` = OK, `false` = Steam lo rechazó, `null` = error de red/timeout: reintentar).
  - `static Task<Dictionary<string, double>?> WebApis.GetGlobalPercentagesAsync(WebBrowser webBrowser, uint appId)`
  - `static Task<StoreInfo?> WebApis.GetStoreInfoAsync(WebBrowser webBrowser, uint appId)` (`null` = fallo de red; `Found=false` = la tienda no lo conoce).

No hay tests automáticos (requieren Steam real); se verifican en la Task 15.

- [ ] **Step 1: Implementar `AchievementDrift/Steam/StatsHandler.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using SteamKit2;
using SteamKit2.Internal;

namespace AchievementDrift.Steam;

internal sealed record StatsSnapshot(IReadOnlyList<SchemaAchievement> Achievements, IReadOnlyDictionary<uint, uint> StatValues, uint CrcStats);

internal sealed class StatsHandler(Bot bot) : ClientMsgHandler {
	private readonly Bot Bot = bot ?? throw new ArgumentNullException(nameof(bot));

	public override void HandleMsg(IPacketMsg packetMsg) {
		ArgumentNullException.ThrowIfNull(packetMsg);

		switch (packetMsg.MsgType) {
			case EMsg.ClientGetUserStatsResponse:
				Client.PostCallback(new GetStatsCallback(packetMsg.TargetJobID, new ClientMsgProtobuf<CMsgClientGetUserStatsResponse>(packetMsg).Body));

				break;
			case EMsg.ClientStoreUserStatsResponse:
				Client.PostCallback(new StoreStatsCallback(packetMsg.TargetJobID, new ClientMsgProtobuf<CMsgClientStoreUserStatsResponse>(packetMsg).Body));

				break;
			default:
				break;
		}
	}

	internal async Task<StatsSnapshot?> GetAsync(uint appId) {
		if (!Client.IsConnected) {
			return null;
		}

		ClientMsgProtobuf<CMsgClientGetUserStats> request = new(EMsg.ClientGetUserStats) {
			SourceJobID = Client.GetNextJobID(),
			Body = {
				game_id = appId,
				steam_id_for_user = Bot.SteamID
			}
		};

		Client.Send(request);

		GetStatsCallback callback;

		try {
			callback = await new AsyncJob<GetStatsCallback>(Client, request.SourceJobID).ToLongRunningTask().ConfigureAwait(false);
		} catch (Exception e) {
			Bot.ArchiLogger.LogGenericWarningException(e);

			return null;
		}

		CMsgClientGetUserStatsResponse response = callback.Response;

		if ((EResult) response.eresult != EResult.OK) {
			Bot.ArchiLogger.LogGenericWarning($"[AchievementDrift] Steam no devolvió estadísticas de {appId}: {(EResult) response.eresult}");

			return null;
		}

		Dictionary<uint, uint> values = [];

		foreach (CMsgClientGetUserStatsResponse.Stats stat in response.stats) {
			values[stat.stat_id] = stat.stat_value;
		}

		if ((response.schema == null) || (response.schema.Length == 0)) {
			return new StatsSnapshot([], values, response.crc_stats);
		}

		KeyValue schema = new();

		using (MemoryStream stream = new(response.schema)) {
			if (!schema.TryReadAsBinary(stream)) {
				Bot.ArchiLogger.LogGenericWarning($"[AchievementDrift] Esquema inválido para {appId}");

				return null;
			}
		}

		return new StatsSnapshot(SchemaParser.Parse(schema, values), values, response.crc_stats);
	}

	internal async Task<bool?> UnlockAsync(uint appId, SchemaAchievement achievement, StatsSnapshot snapshot) {
		ArgumentNullException.ThrowIfNull(achievement);
		ArgumentNullException.ThrowIfNull(snapshot);

		if (!Client.IsConnected) {
			return null;
		}

		ClientMsgProtobuf<CMsgClientStoreUserStats2> request = new(EMsg.ClientStoreUserStats2) {
			SourceJobID = Client.GetNextJobID(),
			Body = {
				game_id = appId,
				settor_steam_id = Bot.SteamID,
				settee_steam_id = Bot.SteamID,
				explicit_reset = false,
				crc_stats = snapshot.CrcStats
			}
		};

		request.Body.stats.Add(new CMsgClientStoreUserStats2.Stats { stat_id = achievement.StatId, stat_value = snapshot.StatValues.GetValueOrDefault(achievement.StatId) | (1u << achievement.Bit) });

		if (achievement.DependencyStatId is uint dependency) {
			request.Body.stats.Add(new CMsgClientStoreUserStats2.Stats { stat_id = dependency, stat_value = Math.Max(snapshot.StatValues.GetValueOrDefault(dependency), achievement.DependencyValue) });
		}

		Client.Send(request);

		try {
			StoreStatsCallback callback = await new AsyncJob<StoreStatsCallback>(Client, request.SourceJobID).ToLongRunningTask().ConfigureAwait(false);

			return (EResult) callback.Response.eresult == EResult.OK;
		} catch (Exception e) {
			Bot.ArchiLogger.LogGenericWarningException(e);

			return null;
		}
	}

	private sealed class GetStatsCallback : CallbackMsg {
		internal CMsgClientGetUserStatsResponse Response { get; }

		internal GetStatsCallback(JobID jobID, CMsgClientGetUserStatsResponse response) {
			ArgumentNullException.ThrowIfNull(jobID);

			JobID = jobID;
			Response = response ?? throw new ArgumentNullException(nameof(response));
		}
	}

	private sealed class StoreStatsCallback : CallbackMsg {
		internal CMsgClientStoreUserStatsResponse Response { get; }

		internal StoreStatsCallback(JobID jobID, CMsgClientStoreUserStatsResponse response) {
			ArgumentNullException.ThrowIfNull(jobID);

			JobID = jobID;
			Response = response ?? throw new ArgumentNullException(nameof(response));
		}
	}
}
```

- [ ] **Step 2: Implementar `AchievementDrift/Steam/WebApis.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using AchievementDrift.Core;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;

namespace AchievementDrift.Steam;

internal static class WebApis {
	internal static async Task<Dictionary<string, double>?> GetGlobalPercentagesAsync(WebBrowser webBrowser, uint appId) {
		ArgumentNullException.ThrowIfNull(webBrowser);

		Uri url = new($"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={appId.ToString(CultureInfo.InvariantCulture)}");
		ObjectResponse<JsonElement>? response = await webBrowser.UrlGetToJsonObject<JsonElement>(url).ConfigureAwait(false);

		if ((response == null) || (response.Content.ValueKind != JsonValueKind.Object)) {
			return null;
		}

		Dictionary<string, double> result = new(StringComparer.Ordinal);

		if (!response.Content.TryGetProperty("achievementpercentages", out JsonElement container) || !container.TryGetProperty("achievements", out JsonElement achievements) || (achievements.ValueKind != JsonValueKind.Array)) {
			return result;
		}

		foreach (JsonElement achievement in achievements.EnumerateArray()) {
			if (!achievement.TryGetProperty("name", out JsonElement name) || (name.GetString() is not { Length: > 0 } apiName) || !achievement.TryGetProperty("percent", out JsonElement percent)) {
				continue;
			}

			// Steam devuelve el porcentaje como número o como cadena según la versión del endpoint
			double? value = percent.ValueKind switch {
				JsonValueKind.Number => percent.GetDouble(),
				JsonValueKind.String when double.TryParse(percent.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
				_ => null
			};

			if (value.HasValue) {
				result[apiName] = value.Value;
			}
		}

		return result;
	}

	internal static async Task<StoreInfo?> GetStoreInfoAsync(WebBrowser webBrowser, uint appId) {
		ArgumentNullException.ThrowIfNull(webBrowser);

		string key = appId.ToString(CultureInfo.InvariantCulture);
		Uri url = new($"https://store.steampowered.com/api/appdetails?appids={key}&l=english");
		ObjectResponse<JsonElement>? response = await webBrowser.UrlGetToJsonObject<JsonElement>(url).ConfigureAwait(false);

		if ((response == null) || (response.Content.ValueKind != JsonValueKind.Object) || !response.Content.TryGetProperty(key, out JsonElement entry)) {
			return null;
		}

		if (!entry.TryGetProperty("success", out JsonElement success) || (success.ValueKind != JsonValueKind.True) || !entry.TryGetProperty("data", out JsonElement data)) {
			return new StoreInfo(false, key, []);
		}

		string name = data.TryGetProperty("name", out JsonElement nameElement) && (nameElement.GetString() is { Length: > 0 } text) ? text : key;
		ImmutableHashSet<int>.Builder categories = ImmutableHashSet.CreateBuilder<int>();

		if (data.TryGetProperty("categories", out JsonElement categoryArray) && (categoryArray.ValueKind == JsonValueKind.Array)) {
			foreach (JsonElement category in categoryArray.EnumerateArray()) {
				if (category.TryGetProperty("id", out JsonElement id) && id.TryGetInt32(out int value)) {
					categories.Add(value);
				}
			}
		}

		return new StoreInfo(true, name, categories.ToImmutable());
	}
}
```

- [ ] **Step 3: Compilar en Release**

Run: `dotnet build AchievementDrift -c Release --nologo`
Expected: 0 errores, 0 warnings. Si un analizador (CAxxxx) falla, corregir el código siguiendo el mensaje (no añadir `NoWarn` global salvo CA5394 ya presente); repetir.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(steam): add stats handler (get/store user stats) and Steam web APIs"
```

---

### Task 10: Almacenamiento, runtime y ciclo de vida del plugin

**Files:**
- Create: `AchievementDrift/Storage/JsonFileStore.cs`, `AchievementDrift/Storage/ConfigStore.cs`, `AchievementDrift/Storage/StoreCache.cs`, `AchievementDrift/Storage/PercentCache.cs`
- Create: `AchievementDrift/Runtime/DriftRuntime.cs`, `AchievementDrift/Runtime/BotRunner.cs`
- Modify: `AchievementDrift/AchievementDriftPlugin.cs`

**Interfaces:**
- Consumes: todo el núcleo; `StatsHandler`, `StatsSnapshot`, `WebApis`.
- Produces:
  - `JsonFileStore.DataDirectory` (= `config/AchievementDrift`), `Task<T?> LoadAsync<T>(string fileName) where T : class`, `Task SaveAsync<T>(string fileName, T value) where T : notnull`.
  - `ConfigStore`: `Task<DriftConfig> GetAsync(string botName)`, `Task SaveAsync(string botName, DriftConfig config)` (lanza `ArgumentException` si `Validate()` falla).
  - `StoreCache`: `Task<StoreInfo?> GetAsync(uint appId)`.
  - `PercentCache`: `Task<Dictionary<string, double>?> GetAsync(uint appId)`.
  - `DriftRuntime` (static): `ConfigStore Configs`, `StoreCache Store`, `PercentCache Percents`, `void RegisterHandler(Bot, StatsHandler)`, `StatsHandler? GetHandler(Bot)`, `Task StartAsync(Bot)`, `Task StopAsync(Bot)`, `BotRunner? GetRunner(Bot)`, `Task<DriftStatus> GetStatusAsync(Bot)`, `Task<BotState> LoadStateAsync(string botName)`.
  - `BotRunner`: `static Task<BotRunner> StartAsync(Bot)`, `Task<T> WithStateAsync<T>(Func<BotState, T> func)`, `Task ReplanAsync(uint appId)`, `ValueTask DisposeAsync()`, `static string StateFileName(string botName)`.

- [ ] **Step 1: `AchievementDrift/Storage/JsonFileStore.cs`**

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using ArchiSteamFarm;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;

namespace AchievementDrift.Storage;

// Guardamos en config/AchievementDrift: las actualizaciones de plugins mueven la carpeta del plugin (spec §11)
internal static class JsonFileStore {
	internal static readonly string DataDirectory = Path.Combine(SharedInfo.ConfigDirectory, AchievementDriftPlugin.PluginName);

	internal static async Task<T?> LoadAsync<T>(string fileName) where T : class {
		ArgumentException.ThrowIfNullOrEmpty(fileName);

		string path = Path.Combine(DataDirectory, fileName);

		if (!File.Exists(path)) {
			return null;
		}

		try {
			string json = await File.ReadAllTextAsync(path).ConfigureAwait(false);

			return json.ToJsonObject<T>();
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericWarning($"[AchievementDrift] No se pudo leer {path}, se usarán valores por defecto: {e.Message}");

			return null;
		}
	}

	internal static async Task SaveAsync<T>(string fileName, T value) where T : notnull {
		ArgumentException.ThrowIfNullOrEmpty(fileName);

		Directory.CreateDirectory(DataDirectory);

		string path = Path.Combine(DataDirectory, fileName);
		string temporaryPath = $"{path}.tmp";

		await File.WriteAllTextAsync(temporaryPath, value.ToJsonText(true)).ConfigureAwait(false);
		File.Move(temporaryPath, path, true);
	}
}
```

- [ ] **Step 2: `AchievementDrift/Storage/ConfigStore.cs`**

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using AchievementDrift.Core;
using ArchiSteamFarm.Core;

namespace AchievementDrift.Storage;

internal sealed class ConfigStore {
	private readonly ConcurrentDictionary<string, DriftConfig> Cache = new(StringComparer.Ordinal);

	internal static string FileName(string botName) => $"{botName}.config.json";

	internal async Task<DriftConfig> GetAsync(string botName) {
		ArgumentException.ThrowIfNullOrEmpty(botName);

		if (Cache.TryGetValue(botName, out DriftConfig? cached)) {
			return cached;
		}

		DriftConfig config = await JsonFileStore.LoadAsync<DriftConfig>(FileName(botName)).ConfigureAwait(false) ?? new DriftConfig();
		IReadOnlyList<string> errors = config.Validate();

		if (errors.Count > 0) {
			ASF.ArchiLogger.LogGenericWarning($"[AchievementDrift] Configuración inválida para {botName} ({string.Join(" ", errors)}); se usan valores por defecto.");
			config = new DriftConfig();
		}

		return Cache.GetOrAdd(botName, config);
	}

	internal async Task SaveAsync(string botName, DriftConfig config) {
		ArgumentException.ThrowIfNullOrEmpty(botName);
		ArgumentNullException.ThrowIfNull(config);

		IReadOnlyList<string> errors = config.Validate();

		if (errors.Count > 0) {
			throw new ArgumentException(string.Join(" ", errors), nameof(config));
		}

		await JsonFileStore.SaveAsync(FileName(botName), config).ConfigureAwait(false);
		Cache[botName] = config;
	}
}
```

- [ ] **Step 3: `AchievementDrift/Storage/StoreCache.cs` y `PercentCache.cs`**

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AchievementDrift.Core;
using AchievementDrift.Steam;
using ArchiSteamFarm.Core;

namespace AchievementDrift.Storage;

internal sealed class StoreCache {
	private const string FileName = "store-cache.json";

	private readonly SemaphoreSlim Lock = new(1, 1);
	private Dictionary<uint, StoreInfo>? Entries;

	internal async Task<StoreInfo?> GetAsync(uint appId) {
		await Lock.WaitAsync().ConfigureAwait(false);

		try {
			Entries ??= await JsonFileStore.LoadAsync<Dictionary<uint, StoreInfo>>(FileName).ConfigureAwait(false) ?? [];

			if (Entries.TryGetValue(appId, out StoreInfo? cached)) {
				return cached;
			}

			if (ASF.WebBrowser == null) {
				return null;
			}

			StoreInfo? info = await WebApis.GetStoreInfoAsync(ASF.WebBrowser, appId).ConfigureAwait(false);

			if (info != null) {
				Entries[appId] = info;
				await JsonFileStore.SaveAsync(FileName, Entries).ConfigureAwait(false);
			}

			return info;
		} finally {
			Lock.Release();
		}
	}
}
```

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using AchievementDrift.Steam;
using ArchiSteamFarm.Core;

namespace AchievementDrift.Storage;

internal sealed class PercentCache {
	private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

	private readonly ConcurrentDictionary<uint, (DateTime FetchedAtUtc, Dictionary<string, double> Percents)> Entries = new();

	internal async Task<Dictionary<string, double>?> GetAsync(uint appId) {
		if (Entries.TryGetValue(appId, out (DateTime FetchedAtUtc, Dictionary<string, double> Percents) cached) && (DateTime.UtcNow - cached.FetchedAtUtc < Lifetime)) {
			return cached.Percents;
		}

		if (ASF.WebBrowser == null) {
			return null;
		}

		Dictionary<string, double>? percents = await WebApis.GetGlobalPercentagesAsync(ASF.WebBrowser, appId).ConfigureAwait(false);

		if (percents != null) {
			Entries[appId] = (DateTime.UtcNow, percents);
		}

		return percents;
	}
}
```

- [ ] **Step 4: `AchievementDrift/Runtime/BotRunner.cs`**

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementDrift.Core;
using AchievementDrift.Steam;
using AchievementDrift.Storage;
using ArchiSteamFarm.Steam;

namespace AchievementDrift.Runtime;

internal sealed class BotRunner : IAsyncDisposable {
	private static readonly TimeSpan PlanRetryDelay = TimeSpan.FromMinutes(30);
	private static readonly TimeSpan TickPeriod = TimeSpan.FromMinutes(1);

	private readonly Bot Bot;
	private readonly DriftEngine Engine = new(Random.Shared);
	private readonly ConcurrentDictionary<uint, EEligibility> LoggedEligibility = new();
	private readonly ConcurrentDictionary<uint, DateTime> PlanRetryAfter = new();
	private readonly SemaphoreSlim StateLock = new(1, 1);

	private readonly BotState State;
	private Timer? Timer;

	private BotRunner(Bot bot, BotState state) {
		Bot = bot;
		State = state;
	}

	internal static string StateFileName(string botName) => $"{botName}.state.json";

	internal static async Task<BotRunner> StartAsync(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		BotState state = await DriftRuntime.LoadStateAsync(bot.BotName).ConfigureAwait(false);
		BotRunner runner = new(bot, state);
		runner.Timer = new Timer(runner.OnTimer, null, TickPeriod, TickPeriod);

		bot.ArchiLogger.LogGenericInfo($"[AchievementDrift] Iniciado ({state.Plans.Count} planes cargados).");

		return runner;
	}

	public async ValueTask DisposeAsync() {
		if (Timer != null) {
			await Timer.DisposeAsync().ConfigureAwait(false);
			Timer = null;
		}
	}

	internal async Task ReplanAsync(uint appId) {
		await StateLock.WaitAsync().ConfigureAwait(false);

		try {
			State.Plans.Remove(appId);
			State.ActiveGames.Remove(appId);
			PlanRetryAfter.TryRemove(appId, out _);
			await JsonFileStore.SaveAsync(StateFileName(Bot.BotName), State).ConfigureAwait(false);
		} finally {
			StateLock.Release();
		}
	}

	internal async Task<T> WithStateAsync<T>(Func<BotState, T> func) {
		ArgumentNullException.ThrowIfNull(func);

		await StateLock.WaitAsync().ConfigureAwait(false);

		try {
			return func(State);
		} finally {
			StateLock.Release();
		}
	}

	private static IReadOnlyCollection<uint> GetPlayingAppIds(Bot bot) {
		// !play no es detectable: ASF no guarda qué juegos envía (spec §11)
		if (bot.CardsFarmer.NowFarming) {
			return bot.CardsFarmer.CurrentGamesFarmingReadOnly.Select(static game => game.AppID).ToHashSet();
		}

		return bot.IsPlayingPossible ? bot.BotConfig.GamesPlayedWhileIdle.ToHashSet() : [];
	}

	private async Task ExecuteUnlockAsync(UnlockDecision decision, DriftConfig config, ActiveWindow window, DateTime nowUtc) {
		GamePlan plan = State.Plans[decision.AppId];
		PlannedAchievement entry = decision.Entry;

		if (config.DryRun) {
			DriftEngine.ApplyResult(entry, EEntryState.Simulated, nowUtc);
			Bot.ArchiLogger.LogGenericInfo($"[AchievementDrift][DryRun] Desbloquearía \"{entry.DisplayName}\" ({entry.GlobalPercent.ToString("0.#", CultureInfo.InvariantCulture)}%) en {plan.GameName}");

			return;
		}

		StatsHandler? handler = DriftRuntime.GetHandler(Bot);
		StatsSnapshot? snapshot = handler == null ? null : await handler.GetAsync(decision.AppId).ConfigureAwait(false);

		if ((handler == null) || (snapshot == null)) {
			// Se reintenta en el próximo tick: el logro sigue vencido
			return;
		}

		await SyncPlanAsync(plan, snapshot, config, window).ConfigureAwait(false);

		if (entry.State != EEntryState.Pending) {
			return;
		}

		SchemaAchievement? target = snapshot.Achievements.FirstOrDefault(achievement => achievement.ApiName == entry.ApiName);

		if ((target == null) || target.IsProtected) {
			DriftEngine.ApplyResult(entry, EEntryState.Skipped, nowUtc);
			Bot.ArchiLogger.LogGenericWarning($"[AchievementDrift] \"{entry.DisplayName}\" en {plan.GameName} ya no existe o está protegido; se salta.");

			return;
		}

		bool? success = await handler.UnlockAsync(decision.AppId, target, snapshot).ConfigureAwait(false);

		switch (success) {
			case true:
				DriftEngine.ApplyResult(entry, EEntryState.Unlocked, nowUtc);
				Bot.ArchiLogger.LogGenericInfo($"[AchievementDrift] Logro desbloqueado: \"{entry.DisplayName}\" ({entry.GlobalPercent.ToString("0.#", CultureInfo.InvariantCulture)}%) en {plan.GameName}");

				break;
			case false:
				DriftEngine.ApplyResult(entry, EEntryState.Skipped, nowUtc);
				Bot.ArchiLogger.LogGenericWarning($"[AchievementDrift] Steam rechazó \"{entry.DisplayName}\" en {plan.GameName}; se salta.");

				break;
			default:
				// Error de red: se reintenta en el próximo tick
				break;
		}
	}

	private async Task<IReadOnlyCollection<uint>> GetEligiblePlayingAsync(DriftConfig config) {
		List<uint> eligible = [];

		foreach (uint appId in GetPlayingAppIds(Bot)) {
			StoreInfo? info = SafetyRules.NeedsStoreInfo(appId, config) ? await DriftRuntime.Store.GetAsync(appId).ConfigureAwait(false) : null;
			EEligibility eligibility = SafetyRules.Evaluate(appId, config, info);

			if (eligibility == EEligibility.Eligible) {
				eligible.Add(appId);
			}

			if (!LoggedEligibility.TryGetValue(appId, out EEligibility previous) || (previous != eligibility)) {
				LoggedEligibility[appId] = eligibility;

				if (eligibility != EEligibility.Eligible) {
					Bot.ArchiLogger.LogGenericInfo($"[AchievementDrift] Se excluye {info?.Name ?? appId.ToString(CultureInfo.InvariantCulture)} ({appId}): {SafetyRules.Describe(eligibility)}.");
				}
			}
		}

		return eligible;
	}

	private void OnTimer(object? state) => _ = TickAsync();

	private async Task SyncPlanAsync(GamePlan plan, StatsSnapshot snapshot, DriftConfig config, ActiveWindow window) {
		Dictionary<string, SchemaAchievement> byName = snapshot.Achievements.DistinctBy(static achievement => achievement.ApiName).ToDictionary(static achievement => achievement.ApiName, StringComparer.Ordinal);

		// Logros desbloqueados fuera del plugin cuentan como hechos
		foreach (PlannedAchievement entry in plan.Entries.Where(static entry => entry.State == EEntryState.Pending)) {
			if (byName.TryGetValue(entry.ApiName, out SchemaAchievement? achievement) && achievement.IsUnlocked) {
				DriftEngine.ApplyResult(entry, EEntryState.Unlocked, DateTime.UtcNow);
			}
		}

		// Logros nuevos (DLC/actualización) se añaden al final del plan
		HashSet<string> planned = plan.Entries.Select(static entry => entry.ApiName).ToHashSet(StringComparer.Ordinal);
		List<SchemaAchievement> added = snapshot.Achievements.Where(achievement => !planned.Contains(achievement.ApiName) && !achievement.IsUnlocked && !achievement.IsProtected).ToList();

		if (added.Count == 0) {
			return;
		}

		Dictionary<string, double> percents = await DriftRuntime.Percents.GetAsync(plan.AppId).ConfigureAwait(false) ?? [];
		List<AchievementInfo> newOnes = added.Select(achievement => new AchievementInfo(achievement.ApiName, achievement.DisplayName, percents.GetValueOrDefault(achievement.ApiName, double.NaN))).ToList();

		PlanGenerator.Append(plan, newOnes, snapshot.Achievements.Count(static achievement => !achievement.IsProtected), config, window.MinutesPerDay, Random.Shared);
		Bot.ArchiLogger.LogGenericInfo($"[AchievementDrift] {newOnes.Count} logros nuevos añadidos al plan de {plan.GameName}.");
	}

	private async Task TickAsync() {
		// Si el tick anterior (o una consulta de estado) sigue en curso, esperamos un poco antes de rendirnos
		if (!await StateLock.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false)) {
			return;
		}

		try {
			if (!Bot.IsConnectedAndLoggedOn) {
				return;
			}

			DriftConfig config = await DriftRuntime.Configs.GetAsync(Bot.BotName).ConfigureAwait(false);
			ActiveWindow window = ActiveWindow.FromConfig(config);
			DateTime nowUtc = DateTime.UtcNow;

			IReadOnlyCollection<uint> eligible = config.Enabled ? await GetEligiblePlayingAsync(config).ConfigureAwait(false) : [];
			TickResult result = Engine.Tick(State, nowUtc, eligible, config, window);
			bool changed = result.StateChanged;

			foreach (uint appId in result.GamesNeedingPlan) {
				changed |= await TryCreatePlanAsync(appId, config, window, nowUtc).ConfigureAwait(false);
			}

			if (result.Unlock != null) {
				await ExecuteUnlockAsync(result.Unlock, config, window, nowUtc).ConfigureAwait(false);
				changed = true;
			}

			if (changed) {
				await JsonFileStore.SaveAsync(StateFileName(Bot.BotName), State).ConfigureAwait(false);
			}
		} catch (Exception e) {
			Bot.ArchiLogger.LogGenericException(e);
		} finally {
			StateLock.Release();
		}
	}

	private async Task<bool> TryCreatePlanAsync(uint appId, DriftConfig config, ActiveWindow window, DateTime nowUtc) {
		if (PlanRetryAfter.TryGetValue(appId, out DateTime retryAfter) && (retryAfter > nowUtc)) {
			return false;
		}

		StatsHandler? handler = DriftRuntime.GetHandler(Bot);
		StatsSnapshot? snapshot = handler == null ? null : await handler.GetAsync(appId).ConfigureAwait(false);
		Dictionary<string, double>? percents = snapshot == null ? null : await DriftRuntime.Percents.GetAsync(appId).ConfigureAwait(false);

		if ((snapshot == null) || (percents == null)) {
			PlanRetryAfter[appId] = nowUtc + PlanRetryDelay;
			Bot.ArchiLogger.LogGenericWarning($"[AchievementDrift] No se pudieron obtener los logros de {appId}; se reintentará en {PlanRetryDelay.TotalMinutes} minutos.");

			return false;
		}

		List<AchievementInfo> pending = snapshot.Achievements
			.Where(static achievement => !achievement.IsUnlocked && !achievement.IsProtected)
			.Select(achievement => new AchievementInfo(achievement.ApiName, achievement.DisplayName, percents.GetValueOrDefault(achievement.ApiName, double.NaN)))
			.ToList();

		int total = snapshot.Achievements.Count(static achievement => !achievement.IsProtected);
		StoreInfo? info = await DriftRuntime.Store.GetAsync(appId).ConfigureAwait(false);
		string gameName = info is { Found: true } ? info.Name : appId.ToString(CultureInfo.InvariantCulture);

		GamePlan plan = PlanGenerator.Create(appId, gameName, pending, total, config, window.MinutesPerDay, Random.Shared, nowUtc);
		State.Plans[appId] = plan;

		int protectedCount = snapshot.Achievements.Count(static achievement => achievement.IsProtected);
		double days = window.MinutesPerDay == 0 ? 0 : plan.PlanMinutes / (double) window.MinutesPerDay;
		Bot.ArchiLogger.LogGenericInfo($"[AchievementDrift] Plan creado para {gameName}: {pending.Count} logros pendientes ({protectedCount} protegidos excluidos) en ~{days.ToString("0", CultureInfo.InvariantCulture)} días de juego.");

		return true;
	}
}
```

- [ ] **Step 5: `AchievementDrift/Runtime/DriftRuntime.cs`**

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using AchievementDrift.Core;
using AchievementDrift.Steam;
using AchievementDrift.Storage;
using ArchiSteamFarm.Steam;

namespace AchievementDrift.Runtime;

internal static class DriftRuntime {
	internal static readonly ConfigStore Configs = new();
	internal static readonly PercentCache Percents = new();
	internal static readonly StoreCache Store = new();

	private static readonly ConcurrentDictionary<Bot, StatsHandler> Handlers = new();
	private static readonly ConcurrentDictionary<Bot, BotRunner> Runners = new();

	internal static StatsHandler? GetHandler(Bot bot) => Handlers.GetValueOrDefault(bot);

	internal static BotRunner? GetRunner(Bot bot) => Runners.GetValueOrDefault(bot);

	internal static async Task<DriftStatus> GetStatusAsync(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		DriftConfig config = await Configs.GetAsync(bot.BotName).ConfigureAwait(false);
		ActiveWindow window = ActiveWindow.FromConfig(config);
		BotRunner? runner = GetRunner(bot);

		if (runner != null) {
			return await runner.WithStateAsync(state => StatusBuilder.Build(bot.BotName, true, config, window, state, DateTime.UtcNow)).ConfigureAwait(false);
		}

		BotState stored = await LoadStateAsync(bot.BotName).ConfigureAwait(false);

		return StatusBuilder.Build(bot.BotName, false, config, window, stored, DateTime.UtcNow);
	}

	internal static async Task<BotState> LoadStateAsync(string botName) => await JsonFileStore.LoadAsync<BotState>(BotRunner.StateFileName(botName)).ConfigureAwait(false) ?? new BotState();

	internal static void RegisterHandler(Bot bot, StatsHandler handler) => Handlers[bot] = handler;

	internal static async Task StartAsync(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		await StopAsync(bot).ConfigureAwait(false);
		Runners[bot] = await BotRunner.StartAsync(bot).ConfigureAwait(false);
	}

	internal static async Task StopAsync(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (Runners.TryRemove(bot, out BotRunner? runner)) {
			await runner.DisposeAsync().ConfigureAwait(false);
		}
	}

	internal static async Task UnregisterAsync(Bot bot) {
		await StopAsync(bot).ConfigureAwait(false);
		Handlers.TryRemove(bot, out _);
	}
}
```

- [ ] **Step 6: Reemplazar `AchievementDrift/AchievementDriftPlugin.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AchievementDrift.Runtime;
using AchievementDrift.Steam;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using JetBrains.Annotations;
using SteamKit2;

namespace AchievementDrift;

#pragma warning disable CA1812 // ASF uses this class during runtime
[UsedImplicitly]
internal sealed class AchievementDriftPlugin : IGitHubPluginUpdates, IBot, IBotConnection, IBotSteamClient, IWebInterface {
	internal const string PluginName = "AchievementDrift";

	public string Name => PluginName;
	public string PhysicalPath => "www";
	public string RepositoryName => "owenunda/ASF-AchievementDrift";
	public Version Version => typeof(AchievementDriftPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));
	public string WebPath => "/drift";

	public Task OnBotDestroy(Bot bot) => DriftRuntime.UnregisterAsync(bot);

	public Task OnBotDisconnected(Bot bot, EResult reason) => DriftRuntime.StopAsync(bot);

	public Task OnBotInit(Bot bot) => Task.CompletedTask;

	public Task OnBotLoggedOn(Bot bot) => DriftRuntime.StartAsync(bot);

	public Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) => Task.CompletedTask;

	public Task<IReadOnlyCollection<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		StatsHandler handler = new(bot);
		DriftRuntime.RegisterHandler(bot, handler);

		return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>([handler]);
	}

	public Task OnLoaded() {
		ASF.ArchiLogger.LogGenericInfo($"{Name} {Version} cargado. Panel web: /drift/");

		return Task.CompletedTask;
	}
}
#pragma warning restore CA1812 // ASF uses this class during runtime
```

Crear también `AchievementDrift/www/index.html` provisional (se reemplaza en la Task 13) para que ASF no avise de ruta inexistente:

```html
<!doctype html>
<html lang="es"><head><meta charset="utf-8"><title>AchievementDrift</title></head><body>AchievementDrift</body></html>
```

- [ ] **Step 7: Compilar en Release y ejecutar tests**

```bash
dotnet build AchievementDrift -c Release --nologo
dotnet test AchievementDrift.Tests --nologo
```

Expected: build limpio; todos los tests PASS. Corregir avisos de analizadores si aparecen.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: wire storage, per-bot runner and plugin lifecycle"
```

---

### Task 11: Comandos `!drift`

**Files:**
- Create: `AchievementDrift/Commands/DriftCommands.cs`
- Modify: `AchievementDrift/AchievementDriftPlugin.cs` (añadir `IBotCommand2`)

**Interfaces:**
- Consumes: `DriftRuntime`, `BotRunner.ReplanAsync`, `BotRunner.WithStateAsync`, `StatusBuilder.FormatText/FormatPlan`, `ConfigStore`.
- Produces: `static Task<string?> DriftCommands.HandleAsync(Bot bot, EAccess access, string[] args)` — devuelve `null` si el comando no es `drift` o no hay permiso `Master`.

- [ ] **Step 1: Implementar `AchievementDrift/Commands/DriftCommands.cs`**

```csharp
using System;
using System.Globalization;
using System.Threading.Tasks;
using AchievementDrift.Core;
using AchievementDrift.Runtime;
using ArchiSteamFarm.Steam;

namespace AchievementDrift.Commands;

internal static class DriftCommands {
	private const string Usage = "Uso: !drift status | on | off | plan <appid> | replan <appid>";

	internal static async Task<string?> HandleAsync(Bot bot, EAccess access, string[] args) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(args);

		if ((args.Length == 0) || !args[0].Equals("drift", StringComparison.OrdinalIgnoreCase) || (access < EAccess.Master)) {
			return null;
		}

		string subcommand = args.Length > 1 ? args[1].ToUpperInvariant() : "STATUS";
		uint appId = 0;
		bool hasAppId = (args.Length > 2) && uint.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out appId) && (appId > 0);

		string response = subcommand switch {
			"STATUS" => StatusBuilder.FormatText(await DriftRuntime.GetStatusAsync(bot).ConfigureAwait(false)),
			"ON" => await SetEnabledAsync(bot, true).ConfigureAwait(false),
			"OFF" => await SetEnabledAsync(bot, false).ConfigureAwait(false),
			"PLAN" when hasAppId => await ShowPlanAsync(bot, appId).ConfigureAwait(false),
			"REPLAN" when hasAppId => await ReplanAsync(bot, appId).ConfigureAwait(false),
			_ => Usage
		};

		return bot.Commands.FormatBotResponse(response);
	}

	private static async Task<string> ReplanAsync(Bot bot, uint appId) {
		BotRunner? runner = DriftRuntime.GetRunner(bot);

		if (runner == null) {
			return "El bot no está conectado.";
		}

		await runner.ReplanAsync(appId).ConfigureAwait(false);

		return $"Plan de {appId} eliminado; se regenerará cuando el juego vuelva a estar activo.";
	}

	private static async Task<string> SetEnabledAsync(Bot bot, bool enabled) {
		DriftConfig config = await DriftRuntime.Configs.GetAsync(bot.BotName).ConfigureAwait(false);
		await DriftRuntime.Configs.SaveAsync(bot.BotName, config with { Enabled = enabled }).ConfigureAwait(false);

		return enabled ? "AchievementDrift activado." : "AchievementDrift desactivado.";
	}

	private static string FormatPlanOrMissing(BotState state, uint appId) => state.Plans.TryGetValue(appId, out GamePlan? plan) ? StatusBuilder.FormatPlan(plan) : $"No hay plan para {appId} todavía.";

	private static async Task<string> ShowPlanAsync(Bot bot, uint appId) {
		BotRunner? runner = DriftRuntime.GetRunner(bot);

		if (runner != null) {
			return await runner.WithStateAsync(state => FormatPlanOrMissing(state, appId)).ConfigureAwait(false);
		}

		BotState stored = await DriftRuntime.LoadStateAsync(bot.BotName).ConfigureAwait(false);

		return FormatPlanOrMissing(stored, appId);
	}
}
```

- [ ] **Step 2: Conectar en el plugin**

En `AchievementDriftPlugin.cs`: añadir `IBotCommand2` a la lista de interfaces, `using AchievementDrift.Commands;` y el método:

```csharp
	public Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0) => DriftCommands.HandleAsync(bot, access, args);
```

- [ ] **Step 3: Compilar en Release**

Run: `dotnet build AchievementDrift -c Release --nologo`
Expected: build limpio.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: add !drift status|on|off|plan|replan commands"
```

---

### Task 12: Endpoints IPC `/Api/Drift`

**Files:**
- Create: `AchievementDrift/IPC/DriftController.cs`

**Interfaces:**
- Consumes: `DriftRuntime.GetStatusAsync`, `DriftRuntime.Configs`, `DriftConfig.Validate`, `DriftStatus`.
- Produces (HTTP, cabecera `Authentication: <IPCPassword>`):
  - `GET /Api/Drift/Bots` → `GenericResponse<IReadOnlyList<string>>`
  - `GET /Api/Drift/{botName}` → `GenericResponse<DriftStatus>`
  - `GET /Api/Drift/{botName}/Config` → `GenericResponse<DriftConfig>`
  - `PUT /Api/Drift/{botName}/Config` (body `DriftConfig`, PascalCase) → `GenericResponse` (400 con mensaje si no valida)

- [ ] **Step 1: Implementar `AchievementDrift/IPC/DriftController.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AchievementDrift.Core;
using AchievementDrift.Runtime;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Steam;
using Microsoft.AspNetCore.Mvc;

namespace AchievementDrift.IPC;

[Route("/Api/Drift")]
public sealed class DriftController : ArchiController {
	/// <summary>Lista los bots configurados.</summary>
	[HttpGet("Bots")]
	[ProducesResponseType<GenericResponse<IReadOnlyList<string>>>((int) HttpStatusCode.OK)]
	public ActionResult<GenericResponse> BotsGet() {
		IReadOnlyList<string> names = Bot.BotsReadOnly?.Keys.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToList() ?? [];

		return Ok(new GenericResponse<IReadOnlyList<string>>(names));
	}

	/// <summary>Lee la configuración de AchievementDrift de un bot.</summary>
	[HttpGet("{botName:required}/Config")]
	[ProducesResponseType<GenericResponse<DriftConfig>>((int) HttpStatusCode.OK)]
	public async Task<ActionResult<GenericResponse>> ConfigGet(string botName) {
		Bot? bot = Bot.GetBot(botName);

		if (bot == null) {
			return BadRequest(new GenericResponse(false, $"El bot {botName} no existe."));
		}

		DriftConfig config = await DriftRuntime.Configs.GetAsync(bot.BotName).ConfigureAwait(false);

		return Ok(new GenericResponse<DriftConfig>(config));
	}

	/// <summary>Guarda la configuración de AchievementDrift de un bot (se aplica al instante).</summary>
	[HttpPut("{botName:required}/Config")]
	[ProducesResponseType<GenericResponse>((int) HttpStatusCode.OK)]
	public async Task<ActionResult<GenericResponse>> ConfigPut(string botName, [FromBody] DriftConfig config) {
		ArgumentNullException.ThrowIfNull(config);

		Bot? bot = Bot.GetBot(botName);

		if (bot == null) {
			return BadRequest(new GenericResponse(false, $"El bot {botName} no existe."));
		}

		IReadOnlyList<string> errors = config.Validate();

		if (errors.Count > 0) {
			return BadRequest(new GenericResponse(false, string.Join(" ", errors)));
		}

		await DriftRuntime.Configs.SaveAsync(bot.BotName, config).ConfigureAwait(false);

		return Ok(new GenericResponse(true));
	}

	/// <summary>Estado de los planes de logros de un bot.</summary>
	[HttpGet("{botName:required}")]
	[ProducesResponseType<GenericResponse<DriftStatus>>((int) HttpStatusCode.OK)]
	public async Task<ActionResult<GenericResponse>> StatusGet(string botName) {
		Bot? bot = Bot.GetBot(botName);

		if (bot == null) {
			return BadRequest(new GenericResponse(false, $"El bot {botName} no existe."));
		}

		DriftStatus status = await DriftRuntime.GetStatusAsync(bot).ConfigureAwait(false);

		return Ok(new GenericResponse<DriftStatus>(status));
	}
}
```

- [ ] **Step 2: Compilar en Release**

Run: `dotnet build AchievementDrift -c Release --nologo`
Expected: build limpio.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(ipc): add /Api/Drift status and config endpoints"
```

---

### Task 13: Página web `/drift/` (estado, configuración, ayuda)

**Files:**
- Modify: `AchievementDrift/www/index.html`
- Create: `AchievementDrift/www/app.js`, `AchievementDrift/www/style.css`

**Interfaces:**
- Consumes: endpoints de la Task 12. JSON en PascalCase. Respuesta ASF: `{ "Success": bool, "Message": string, "Result": ... }`.

- [ ] **Step 1: `AchievementDrift/www/index.html`**

```html
<!doctype html>
<html lang="es">
<head>
	<meta charset="utf-8">
	<meta name="viewport" content="width=device-width, initial-scale=1">
	<title>AchievementDrift</title>
	<link rel="stylesheet" href="style.css">
</head>
<body>
	<header>
		<h1>AchievementDrift</h1>
		<label>Bot <select id="bot"></select></label>
		<nav>
			<button data-tab="status" class="active">Estado</button>
			<button data-tab="config">Configuración</button>
			<button data-tab="help">Ayuda</button>
		</nav>
	</header>

	<main>
		<p id="message" role="status" hidden></p>

		<section id="tab-status">
			<div id="summary"></div>
			<div id="games"></div>
		</section>

		<section id="tab-config" hidden>
			<div id="tz-suggestion" hidden></div>
			<form id="config-form">
				<label><input type="checkbox" name="Enabled"> Activado</label>
				<label><input type="checkbox" name="DryRun"> Modo simulación (DryRun): no desbloquea, solo registra</label>
				<label>Días mínimos por juego <input type="number" name="MinDays" min="1" max="365" required></label>
				<label>Días máximos por juego <input type="number" name="MaxDays" min="1" max="365" required></label>
				<label>Horario activo (HH:mm-HH:mm) <input type="text" name="ActiveHours" pattern="\d{1,2}:\d{2}-\d{1,2}:\d{2}" required></label>
				<label>Zona horaria (IANA, vacío = servidor) <input type="text" name="TimeZone" placeholder="America/Bogota"></label>
				<label>Juegos en progreso a la vez <input type="number" name="MaxActiveGames" min="1" max="10" required></label>
				<label><input type="checkbox" name="ExcludeVac"> Excluir juegos con VAC</label>
				<label><input type="checkbox" name="ExcludeOnline"> Excluir juegos online/multijugador</label>
				<label>Blacklist (appids separados por coma) <input type="text" name="Blacklist"></label>
				<label>Allowlist (appids separados por coma) <input type="text" name="Allowlist"></label>
				<button type="submit">Guardar</button>
			</form>
		</section>

		<section id="tab-help" hidden>
			<h2>¿Qué hace?</h2>
			<p>Mientras ASF juega tus juegos (<code>GamesPlayedWhileIdle</code> o farmeo de cromos), AchievementDrift desbloquea sus logros poco a poco y en orden aleatorio, para que el progreso se vea natural.</p>
			<h2>Cómo decide</h2>
			<ul>
				<li>Cada juego recibe un <b>plan</b> la primera vez que se juega. Los logros comunes tienden a salir antes y los raros (menor % global) al final, con más espera.</li>
				<li>Un juego nunca se completa en menos de <b>días mínimos</b>: el reloj solo avanza mientras el juego se juega y dentro de tu <b>horario activo</b>.</li>
				<li>Solo hay <b>N juegos en progreso</b> a la vez; los demás esperan su turno.</li>
				<li>Como máximo se desbloquea un logro por minuto.</li>
			</ul>
			<h2>Leer el estado</h2>
			<ul>
				<li>▶️ Activo: avanzando ahora. ⏸️ Pausado: no se está jugando o espera turno. 🏁 Completado.</li>
				<li>"% del plan" es el tiempo de juego acumulado frente al tiempo total planificado.</li>
				<li>La hora estimada del siguiente logro supone que el juego se sigue jugando.</li>
			</ul>
			<h2>Opciones</h2>
			<ul>
				<li><b>DryRun</b>: viene activado. Revisa el log de ASF (líneas <code>[AchievementDrift][DryRun]</code>) y desactívalo cuando estés conforme. Al desactivarlo, los planes simulados se regeneran.</li>
				<li><b>Excluir VAC / online</b>: usa las categorías de la tienda de Steam. Algunos juegos online no las declaran: añádelos a la Blacklist.</li>
				<li><b>Allowlist</b>: fuerza un juego aunque la tienda lo marque como online (bajo tu responsabilidad).</li>
			</ul>
			<h2>Riesgos</h2>
			<p>Modificar logros va contra los términos de Steam. En juegos con anti-cheat o estadísticas validadas por servidor puede haber sanciones dentro del juego. Los logros protegidos por servidor se saltan automáticamente.</p>
			<h2>Problemas comunes</h2>
			<ul>
				<li><b>No aparece ningún plan</b>: comprueba que estás dentro del horario, que el juego está en <code>GamesPlayedWhileIdle</code> y que no está excluido (el log dice por qué).</li>
				<li><b>Uso <code>!play</code></b>: no es detectable por plugins; usa <code>GamesPlayedWhileIdle</code>.</li>
				<li><b>401</b>: la contraseña IPC es incorrecta; recarga la página para introducirla de nuevo.</li>
			</ul>
		</section>
	</main>

	<script src="app.js"></script>
</body>
</html>
```

- [ ] **Step 2: `AchievementDrift/www/style.css`**

```css
:root {
	--bg: #f6f7f9;
	--fg: #1c1f24;
	--muted: #5f6672;
	--card: #ffffff;
	--border: #d9dde3;
	--accent: #2f6fd6;
	--ok: #2e8b57;
	--warn: #b7791f;
}

@media (prefers-color-scheme: dark) {
	:root {
		--bg: #15171a;
		--fg: #e6e8eb;
		--muted: #9aa1ab;
		--card: #1e2125;
		--border: #30353c;
		--accent: #5b93ef;
	}
}

* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--fg); font: 15px/1.5 system-ui, sans-serif; }
header { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; padding: 12px 16px; border-bottom: 1px solid var(--border); background: var(--card); }
header h1 { font-size: 18px; margin: 0 auto 0 0; }
nav { display: flex; gap: 4px; }
nav button { border: 1px solid var(--border); background: transparent; color: var(--fg); padding: 6px 12px; border-radius: 6px; cursor: pointer; }
nav button.active { background: var(--accent); border-color: var(--accent); color: #fff; }
main { max-width: 960px; margin: 0 auto; padding: 16px; }
#message { padding: 8px 12px; border-radius: 6px; border: 1px solid var(--warn); }
#summary { color: var(--muted); margin-bottom: 12px; }
#games { display: grid; gap: 12px; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); }
.card { background: var(--card); border: 1px solid var(--border); border-radius: 8px; padding: 12px; }
.card h3 { margin: 0 0 4px; font-size: 15px; }
.card .meta { color: var(--muted); font-size: 13px; }
.bar { height: 6px; background: var(--border); border-radius: 3px; overflow: hidden; margin: 8px 0; }
.bar > span { display: block; height: 100%; background: var(--ok); }
form { display: grid; gap: 10px; max-width: 520px; }
form label { display: grid; gap: 4px; }
form label:has(input[type=checkbox]) { display: flex; gap: 8px; align-items: center; }
input[type=text], input[type=number], select { padding: 6px 8px; border: 1px solid var(--border); border-radius: 6px; background: var(--card); color: var(--fg); }
button[type=submit], #tz-suggestion button { justify-self: start; padding: 8px 16px; border: 0; border-radius: 6px; background: var(--accent); color: #fff; cursor: pointer; }
#tz-suggestion { margin-bottom: 12px; padding: 8px 12px; border: 1px dashed var(--accent); border-radius: 6px; }
code { font-size: 13px; }
```

- [ ] **Step 3: `AchievementDrift/www/app.js`**

```js
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
```

- [ ] **Step 4: Verificar que `www` se copia a la salida**

```bash
dotnet build AchievementDrift -c Release --nologo
ls AchievementDrift/bin/Release/net10.0/www
```

Expected: `app.js index.html style.css`.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(web): add /drift/ panel with status, config, time zone detection and help"
```

---

### Task 14: Documentación (README + DEVELOPMENT) y publicación

**Files:**
- Create: `README.md`, `docs/DEVELOPMENT.md`

**Interfaces:** ninguna (documentación).

- [ ] **Step 1: Escribir `README.md`** (español) con estas secciones y contenido concreto:

1. **Título y descripción**: "ASF-AchievementDrift — Plugin para ArchiSteamFarm que desbloquea logros de forma gradual y realista mientras farmeas horas."
2. **⚠️ Aviso**: modificar logros va contra los términos de Steam; riesgo en juegos con anti-cheat/estadísticas de servidor; úsalo bajo tu responsabilidad; `DryRun` activo por defecto.
3. **Qué hace**: lista de requisitos del spec §1 (raros al final, ≥ `MinDays` días, aleatorio, solo juegos en reproducción, horario humano, máx. `MaxActiveGames`, exclusiones).
4. **Cómo funciona el algoritmo**: los 4 pasos del spec §3 con un ejemplo numérico: horario 10:00–01:00 (900 min/día), `MinDays=30`, `MaxDays=45` → presupuesto entre 27 000 y 40 500 minutos activos; un logro del 2% espera ~6× más que uno del 80%; el 2% sale después del 80% en ~97% de los planes.
5. **Requisitos**: ASF 6.3.10.5 o compatible; recomendado build `generic` (las builds por SO están recortadas y pueden dar `MissingMethodException`); farmear horas con `GamesPlayedWhileIdle`.
6. **Instalación**: descargar `AchievementDrift.zip` del último release → extraer en `plugins/AchievementDrift/` → reiniciar ASF → comprobar en el log `AchievementDrift ... cargado` y en la pestaña Plugins del panel. Docker: montar/copiar a `/app/plugins/AchievementDrift`.
7. **Primeros pasos**: abrir `https://<tu-asf>/drift/` (con barra final), introducir `IPCPassword`, aceptar la zona horaria detectada, revisar el log `[AchievementDrift][DryRun]`, desactivar `DryRun` cuando esté conforme.
8. **Configuración**: tabla de las 11 claves (spec §5) con valor por defecto y descripción; archivo `config/AchievementDrift/<bot>.config.json`; editable desde la web o la IPC; cambios en caliente.
9. **Comandos**: `!drift status`, `!drift on`, `!drift off`, `!drift plan <appid>`, `!drift replan <appid>` (permiso Master; actúan sobre el bot que recibe el comando).
10. **API IPC**: los 4 endpoints de la Task 12 con ejemplo `curl -H "Authentication: <IPCPassword>" https://<tu-asf>/Api/Drift/<bot>`; aparecen en Swagger.
11. **Archivos que crea**: `config/AchievementDrift/<bot>.config.json`, `<bot>.state.json`, `store-cache.json`; cómo reiniciar el progreso (borrar `<bot>.state.json` con ASF detenido).
12. **Preguntas frecuentes**: "¿Por qué no avanza?" (horario, juego excluido, no está en `GamesPlayedWhileIdle`, `MaxActiveGames` ocupado); "¿Uso `!play`?" (no detectable); "¿Por qué se excluyó un juego?" (mensaje en el log + Allowlist/Blacklist); "¿Qué pasa con logros protegidos?" (se saltan); "Cambié el horario y va más rápido" (los planes están en minutos activos; ampliar el horario acelera el calendario).
13. **Compilar desde el código**: `git clone --recursive https://github.com/owenunda/ASF-AchievementDrift.git`, `dotnet test AchievementDrift.Tests`, `dotnet publish AchievementDrift -c Release -o out`. Enlace a `docs/DEVELOPMENT.md`.
14. **Licencia**: Apache-2.0.

- [ ] **Step 2: Escribir `docs/DEVELOPMENT.md`** con:

1. **Arquitectura**: la tabla de unidades (spec §2 + File Structure de este plan) y el flujo de un tick (lectura de juegos → `SafetyRules` → `DriftEngine.Tick` → crear planes → desbloquear → guardar).
2. **Notas técnicas de ASF 6.3.10.5**: copiar íntegra la sección §11 del spec (detección de juegos, SteamKit2, almacenamiento, JSON, IPC/web, build).
3. **Decisiones**: MSTest en vez de xUnit (versión ya fijada por ASF); configuración solo en el plugin (no en el JSON del bot) para tener una única fuente; `!play` no soportado; estados `Simulated` y regeneración al salir de DryRun; logros desbloqueados fuera del plugin se marcan `Unlocked` al sincronizar.
4. **Desarrollo local**: requisitos (.NET 10 SDK), `git submodule update --init --recursive`, compilar, tests (`dotnet test AchievementDrift.Tests`), cómo probar contra un ASF local (la build copia el plugin a `ArchiSteamFarm/ArchiSteamFarm/bin/<config>/net10.0/plugins/AchievementDrift/`).
5. **Publicar una versión**: subir `<Version>` en `Directory.Build.props` → commit → `git tag X.Y.Z.W` (debe coincidir con `Version`) → `git push --tags` → el workflow `publish.yml` crea el release con `AchievementDrift.zip` → ASF lo actualiza solo (`IGitHubPluginUpdates`, ajustes `PluginsUpdateMode`/`PluginsUpdateList`).
6. **Actualizar ASF**: el workflow `bump-asf-reference.yml` mueve el submódulo a la última versión estable; revisar el changelog de ASF y que los tests pasen antes de publicar.

- [ ] **Step 3: Verificación final local**

```bash
dotnet build -c Release --nologo
dotnet test AchievementDrift.Tests -c Release --nologo
dotnet publish AchievementDrift -c Release -o out/AchievementDrift --nologo
ls out/AchievementDrift
```

Expected: build y tests verdes; `out/AchievementDrift` contiene `AchievementDrift.dll` y `www/` (y **no** `ArchiSteamFarm.dll` ni `SteamKit2.dll`). Si aparecen DLLs de ASF/SteamKit2, revisar `IncludeAssets="compile"` / `Private="false"`.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "docs: add README (usuario) and DEVELOPMENT (técnico)"
```

---

### Task 15: Verificación en el ASF real del usuario (manual, con el usuario)

**Files:** ninguno (solo despliegue y observación).

- [ ] **Step 1: Empaquetar**

```bash
cd out/AchievementDrift && zip -r ../AchievementDrift.zip . && cd ../..
```

(En PowerShell: `Compress-Archive -Path out/AchievementDrift/* -DestinationPath out/AchievementDrift.zip -Force`.)

- [ ] **Step 2: Pedir al usuario que lo instale** en `plugins/AchievementDrift/` de su ASF (`steam.oween.software`, build `linux-x64`) y reinicie ASF. El usuario hace la copia; no subir nada sin su confirmación.

- [ ] **Step 3: Comprobar carga**: en el log de ASF `AchievementDrift 1.0.0.0 cargado`; en la pestaña Plugins aparece `AchievementDrift`. Si hay `MissingMethodException`/`ReflectionTypeLoadException`, recomendar la build `generic` de ASF y repetir.

- [ ] **Step 4: Comprobar web e IPC**: abrir `https://steam.oween.software/drift/` → introducir IPCPassword → aparece el bot `manticora`, aceptar zona horaria sugerida, guardar configuración (verificar que persiste tras recargar).

- [ ] **Step 5: Comprobar exclusiones**: en el log deben aparecer exclusiones de juegos online de su lista (HELLDIVERS 2 553850, ARC Raiders 1808500, Sea of Thieves 1172620, etc.) y la creación de como máximo 2 planes en juegos elegibles (dentro del horario).

- [ ] **Step 6: Comprobar DryRun**: `!drift status` y `!drift plan <appid>` muestran planes coherentes (raros al final, duración ≥ 30 días); tras el primer offset vencido aparece `[AchievementDrift][DryRun] Desbloquearía ...`.

- [ ] **Step 7: Modo real (decisión del usuario)**: cuando el usuario lo confirme, desactivar `DryRun` desde la web; verificar que los planes simulados se regeneran y que el primer desbloqueo real aparece en el log y en el perfil de Steam.

- [ ] **Step 8: Publicar** (solo con confirmación explícita del usuario): `git push -u origin main`, luego `git tag 1.0.0.0 && git push origin 1.0.0.0`; comprobar que el workflow crea el release con `AchievementDrift.zip`; poner descripción y topics del repo (spec §9).
