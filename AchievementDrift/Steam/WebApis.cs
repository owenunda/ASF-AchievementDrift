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

		if (!TryGetProperty(response.Content, "achievementpercentages", out JsonElement container) || !TryGetProperty(container, "achievements", out JsonElement achievements) || (achievements.ValueKind != JsonValueKind.Array)) {
			return result;
		}

		foreach (JsonElement achievement in achievements.EnumerateArray()) {
			if (!TryGetProperty(achievement, "name", out JsonElement name) || (name.GetString() is not { Length: > 0 } apiName) || !TryGetProperty(achievement, "percent", out JsonElement percent)) {
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

		if ((response == null) || (response.Content.ValueKind != JsonValueKind.Object) || !TryGetProperty(response.Content, key, out JsonElement entry)) {
			return null;
		}

		if (!TryGetProperty(entry, "success", out JsonElement success) || (success.ValueKind != JsonValueKind.True) || !TryGetProperty(entry, "data", out JsonElement data)) {
			return new StoreInfo(false, key, []);
		}

		string name = TryGetProperty(data, "name", out JsonElement nameElement) && (nameElement.GetString() is { Length: > 0 } text) ? text : key;
		ImmutableHashSet<int>.Builder categories = ImmutableHashSet.CreateBuilder<int>();

		if (TryGetProperty(data, "categories", out JsonElement categoryArray) && (categoryArray.ValueKind == JsonValueKind.Array)) {
			foreach (JsonElement category in categoryArray.EnumerateArray()) {
				if (TryGetProperty(category, "id", out JsonElement id) && id.TryGetInt32(out int value)) {
					categories.Add(value);
				}
			}
		}

		return new StoreInfo(true, name, categories.ToImmutable());
	}

	// JsonElement.TryGetProperty no existe en builds recortadas de ASF; EnumerateObject sí
	private static bool TryGetProperty(JsonElement element, string name, out JsonElement value) {
		if (element.ValueKind == JsonValueKind.Object) {
			foreach (JsonProperty property in element.EnumerateObject()) {
				if (property.Name == name) {
					value = property.Value;

					return true;
				}
			}
		}

		value = default;

		return false;
	}
}
