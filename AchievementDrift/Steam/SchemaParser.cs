using System;
using System.Collections.Generic;
using System.Globalization;
using AchievementDrift.Core;
using SteamKit2;

namespace AchievementDrift.Steam;

// Description/Icon/IconGray = "" cuando el esquema no los trae; Icon/IconGray son nombres de archivo del CDN de Steam
internal sealed record SchemaAchievement(string ApiName, string DisplayName, uint StatId, int Bit, bool IsUnlocked, bool IsProtected, uint? DependencyStatId, uint DependencyValue, string Description, string Icon, string IconGray);

// Interpreta el esquema KeyValues de ClientGetUserStatsResponse (ver spec §11)
internal static class SchemaParser {
	// Nombres y descripciones en el idioma activo; inglés como respaldo
	private static readonly string[] EnglishLanguages = ["english"];
	private static readonly string[] SpanishLanguages = ["spanish", "latam", "english"];

	internal static List<SchemaAchievement> Parse(KeyValue schema, IReadOnlyDictionary<uint, uint> statValues, CultureInfo? culture = null) {
		ArgumentNullException.ThrowIfNull(schema);
		ArgumentNullException.ThrowIfNull(statValues);

		string[] languages = Texts.IsSpanish(culture) ? SpanishLanguages : EnglishLanguages;
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

				KeyValue display = bitNode["display"];

				result.Add(
					new SchemaAchievement(
						apiName,
						GetLocalized(display["name"], languages) ?? apiName,
						statId,
						bit,
						(value & (1u << bit)) != 0,
						isProtected,
						dependencyStatId,
						dependencyValue,
						GetLocalized(display["desc"], languages) ?? "",
						display["icon"].Value ?? "",
						display["icon_gray"].Value ?? ""
					)
				);
			}
		}

		return result;
	}

	private static string? GetLocalized(KeyValue texts, string[] languages) {
		foreach (string language in languages) {
			string? text = texts[language].Value;

			if (!string.IsNullOrWhiteSpace(text)) {
				return text;
			}
		}

		return null;
	}

	private static int ParseInt(string? text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

	private static bool TryParseUInt(string? text, out uint value) => uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
