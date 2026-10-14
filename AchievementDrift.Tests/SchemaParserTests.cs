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
	public void ParsesDescriptionAndIcons() {
		KeyValue schema = Node(
			"440",
			Node(
				"stats",
				Node(
					"1",
					Leaf("type", "4"),
					Node(
						"bits",
						Node(
							"0",
							Leaf("name", "ACH_ICON"),
							Node(
								"display",
								Node("name", Leaf("english", "Icon")),
								Node("desc", Leaf("english", "Do it"), Leaf("spanish", "Hazlo")),
								Leaf("icon", "abc.jpg"),
								Leaf("icon_gray", "abc_gray.jpg")
							)
						)
					)
				)
			)
		);

		SchemaAchievement achievement = SchemaParser.Parse(schema, new Dictionary<uint, uint>()).Single();

		Assert.AreEqual("Hazlo", achievement.Description);
		Assert.AreEqual("abc.jpg", achievement.Icon);
		Assert.AreEqual("abc_gray.jpg", achievement.IconGray);
	}

	[TestMethod]
	public void MissingDescriptionAndIconsAreEmpty() {
		SchemaAchievement achievement = Parse().Single(static a => a.ApiName == "ACH_SECOND");

		Assert.AreEqual("", achievement.Description);
		Assert.AreEqual("", achievement.Icon);
		Assert.AreEqual("", achievement.IconGray);
	}

	[TestMethod]
	public void EmptySchemaReturnsEmpty() {
		Assert.AreEqual(0, SchemaParser.Parse(new KeyValue("root"), new Dictionary<uint, uint>()).Count);
	}
}
