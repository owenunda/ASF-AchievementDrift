using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AchievementDrift.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed partial class TextsTests {
	private static readonly CultureInfo English = new("en-US");
	private static readonly CultureInfo Spanish = new("es-ES");

	private static string[] Placeholders(string text) => PlaceholderRegex().Matches(text).Select(static match => match.Groups[1].Value).Distinct().Order().ToArray();

	[GeneratedRegex(@"\{(\d+)(:[^}]*)?\}")]
	private static partial Regex PlaceholderRegex();

	[TestMethod]
	public void BothLanguagesDefineEveryKey() {
		foreach (ETextKey key in Enum.GetValues<ETextKey>()) {
			Assert.IsTrue(Texts.English.ContainsKey(key), $"en: {key}");
			Assert.IsTrue(Texts.Spanish.ContainsKey(key), $"es: {key}");
		}
	}

	[TestMethod]
	public void BothLanguagesUseTheSamePlaceholders() {
		foreach (ETextKey key in Enum.GetValues<ETextKey>()) {
			CollectionAssert.AreEqual(Placeholders(Texts.English[key]), Placeholders(Texts.Spanish[key]), key.ToString());
		}
	}

	[TestMethod]
	[DataRow("es-ES", true)]
	[DataRow("es-CO", true)]
	[DataRow("en-US", false)]
	[DataRow("de-DE", false)]
	public void SpanishOnlyForSpanishCultures(string culture, bool expected) => Assert.AreEqual(expected, Texts.IsSpanish(new CultureInfo(culture)));

	[TestMethod]
	public void InvariantCultureIsEnglish() => Assert.IsFalse(Texts.IsSpanish(CultureInfo.InvariantCulture));

	[TestMethod]
	public void FormatUsesInvariantNumbers() {
		Assert.AreEqual("[AchievementDrift] Achievement unlocked: \"A\" (61.5%) in G", Texts.FormatFor(English, ETextKey.LogUnlocked, "A", 61.5, "G"));
		Assert.AreEqual("[AchievementDrift] Logro desbloqueado: \"A\" (61.5%) en G", Texts.FormatFor(Spanish, ETextKey.LogUnlocked, "A", 61.5, "G"));
	}
}
