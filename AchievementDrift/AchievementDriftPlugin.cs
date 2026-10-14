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
