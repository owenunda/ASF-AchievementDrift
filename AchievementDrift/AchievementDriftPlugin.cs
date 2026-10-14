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
