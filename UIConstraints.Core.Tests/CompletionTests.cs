using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UIConstraints.Core.Completion;
using UIConstraints.Core.Parsing;

namespace UIConstraints.Core.Tests {
public class CompletionTests {
	private const string TEST_GAME = "UIConstraints.Core.Tests.TestGame";

	// the cursor position is marked with '|'
	private static List<AutoCompleteOption> GetOptions(string inputWithCursor, bool explicitRequest = false, params string[] usings) {
		int cursor = inputWithCursor.IndexOf('|');
		return AutoComplete.GetOptions(inputWithCursor.Replace("|", ""), cursor, new ParseContext(usings), explicitRequest, out _);
	}

	private static List<string> GetLabels(string inputWithCursor, bool explicitRequest = false, params string[] usings) {
		return GetOptions(inputWithCursor, explicitRequest, usings).Select(o => o.label).ToList();
	}

	[Test]
	public void InstanceMembers() {
		List<string> labels = GetLabels("player.|");
		Assert.That(labels, Is.SupersetOf(new[] { "name", "inventory", "GetHealthPercent()", "Add(int a, int b)", "Add(float a, float b)" }));
		Assert.That(labels, Does.Not.Contain("Instance")); // static
	}

	[Test]
	public void PrefixIsReplaced() {
		List<AutoCompleteOption> options = GetOptions("player.na|");
		Assert.That(options[0].label, Is.EqualTo("name"));
		Assert.That(options[0].insertText, Is.EqualTo("name"));
	}

	[Test]
	public void MethodsInsertParentheses() {
		AutoCompleteOption option = GetOptions("player.ADD|").First(o => o.label == "Add(int a, int b)");
		Assert.That(option.insertText, Is.EqualTo("Add()"));
		Assert.That(option.cursorOffset, Is.EqualTo(-1)); // inside the parentheses
	}

	[Test]
	public void SubstringMatches() {
		Assert.That(GetLabels("player.health|"), Is.SupersetOf(new[] { "currentHealth", "maxHealth" }));
	}

	[Test]
	public void CompletelyTypedFieldIsNotSuggested() {
		Assert.That(GetLabels("player.name|"), Does.Not.Contain("name"));
	}

	[TestCase("player.GetItem(1).|")]
	[TestCase("1 + player.weapon.|")]
	public void MembersOfChains(string input) {
		Assert.That(GetLabels(input), Does.Contain("name"));
	}

	[TestCase("Math.Max(player.|")]
	[TestCase("Math.Max(player.cur|, 2)")]
	public void MembersInsideUnfinishedArguments(string input) {
		Assert.That(GetLabels(input, false, "System"), Does.Contain("currentHealth"));
	}

	[Test]
	public void StaticMembers() {
		Assert.That(GetLabels("Math.|", false, "System"), Is.SupersetOf(new[] { "PI", "Max(int val1, int val2)" }));
		List<string> labels = GetLabels("Player.|", false, TEST_GAME);
		Assert.That(labels, Is.SupersetOf(new[] { "Instance", "StaticCount", "Nested" }));
		Assert.That(labels, Does.Not.Contain("name"));
	}

	[Test]
	public void DataKeysAtStart() {
		Assert.That(GetOptions("pl|")[0].insertText, Is.EqualTo("player"));
		Assert.That(GetLabels("|"), Is.SupersetOf(new[] { "player", "showInventory" }));
		Assert.That(GetLabels("1 + |"), Does.Contain("player"));
	}

	[Test]
	public void TypesNeedAPrefix() {
		Assert.That(GetLabels("|"), Does.Not.Contain("Math"));
		Assert.That(GetLabels("GameSt|"), Does.Contain("GameState"));
	}

	[Test]
	public void TypesFromOtherNamespacesRequireAUsing() {
		Assert.That(GetOptions("GameSt|").First(o => o.label == "GameState").requiredNamespace, Is.EqualTo(TEST_GAME));
		Assert.That(GetOptions("GameSt|", false, TEST_GAME).First(o => o.label == "GameState").requiredNamespace, Is.Null);
	}

	[TestCase("player |")] // directly after another value
	[TestCase("\"pla|")] // inside a string
	[TestCase("item.|")] // type not known before evaluation
	public void NoSuggestions(string input) {
		Assert.That(GetLabels(input, true), Is.Empty);
	}

	[Test]
	public void Keywords() {
		Assert.That(GetLabels("tr|"), Does.Contain("true"));
	}
}
}
