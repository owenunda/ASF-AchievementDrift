using AchievementDrift.Core;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Threading.Tasks;
using AchievementDrift.Commands;
using AchievementDrift.Runtime;
using AchievementDrift.Steam;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using SteamKit2;

namespace AchievementDrift;

#pragma warning disable CA1812 // ASF uses this class during runtime
[UsedImplicitly]
internal sealed class AchievementDriftPlugin : IGitHubPluginUpdates, IBot, IBotCommand2, IBotConnection, IBotSteamClient, IWebInterface, IWebServiceProvider {
	internal const string PluginName = "AchievementDrift";

	public string Name => PluginName;
	public string PhysicalPath => "www";
	public string RepositoryName => "owenunda/ASF-AchievementDrift";
	public Version Version => typeof(AchievementDriftPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));
	public string WebPath => "/drift";

	public Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0) {
		if (!RuntimeSelfTest.Supported) {
			bool isDrift = (args.Length > 0) && args[0].Equals("drift", StringComparison.OrdinalIgnoreCase) && (access >= EAccess.Master);

			return Task.FromResult(isDrift ? Texts.Format(ETextKey.LogTrimmed, RuntimeSelfTest.Missing) : null);
		}

		return DriftCommands.HandleAsync(bot, access, args, steamID);
	}

	public Task OnBotDestroy(Bot bot) => DriftRuntime.UnregisterAsync(bot);

	public Task OnBotDisconnected(Bot bot, EResult reason) => DriftRuntime.StopAsync(bot);

	public Task OnBotInit(Bot bot) => Task.CompletedTask;

	// Si a la build le falta algo que el plugin necesita (ver RuntimeSelfTest), el runtime fallaría: mejor no arrancarlo
	public Task OnBotLoggedOn(Bot bot) => RuntimeSelfTest.Supported ? DriftRuntime.StartAsync(bot) : Task.CompletedTask;

	public Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) => Task.CompletedTask;

	public Task<IReadOnlyCollection<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		StatsHandler handler = new(bot);
		DriftRuntime.RegisterHandler(bot, handler);

		return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>([handler]);
	}

	public Task OnLoaded() {
		// ASF ya aplicó su CurrentCulture: será el idioma del log, aunque luego lleguen peticiones del panel en otro idioma
		Texts.LogCulture = CultureInfo.CurrentUICulture;

		ASF.ArchiLogger.LogGenericInfo(Texts.Log(ETextKey.LogLoaded, Name, Version, StatusBuilder.PanelPath));
		RuntimeSelfTest.Run();

		return Task.CompletedTask;
	}

	// ASF registra los controladores de todos los plugins y MVC lee sus atributos al hacer MapControllers: si a la build le
	// falta alguno (como pasaba con HttpPutAttribute en win-x64), revienta todo el IPC de ASF. Este callback llega justo
	// antes, así que si el self-test falló quitamos nuestro ensamblado para que solo se pierda la API del plugin.
	public void OnConfiguringEndpoints(IApplicationBuilder app) {
		ArgumentNullException.ThrowIfNull(app);

		if (RuntimeSelfTest.Supported) {
			return;
		}

		ApplicationPartManager partManager = app.ApplicationServices.GetRequiredService<ApplicationPartManager>();

		for (int i = partManager.ApplicationParts.Count - 1; i >= 0; i--) {
			if (partManager.ApplicationParts[i] is AssemblyPart part && (part.Assembly == typeof(AchievementDriftPlugin).Assembly)) {
				partManager.ApplicationParts.RemoveAt(i);
			}
		}
	}

	public void OnConfiguringServices(IServiceCollection services) { }
}
#pragma warning restore CA1812 // ASF uses this class during runtime
