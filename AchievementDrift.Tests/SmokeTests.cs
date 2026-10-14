using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AchievementDrift.Tests;

[TestClass]
public sealed class SmokeTests {
	[TestMethod]
#pragma warning disable MSTEST0032 // Intentional: smoke test only verifies that the test runner works
	public void TestRunnerWorks() => Assert.IsTrue(true);
#pragma warning restore MSTEST0032
}
