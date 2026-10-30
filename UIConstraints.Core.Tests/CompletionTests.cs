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
		return AutoComplete.GetOptions(inputWithCursor.Replace("|", ""), cursor, new ParseContext(usings, EvaluationTests.VARIABLES), explicitRequest, out _);
	}

	private static List<string> GetLabels(string inputWithCursor, bool explicitRequest = false, params string[] usings) {
		return GetOptions(inputWithCursor, explicitRequest, usings).Select(o => o.label).ToList();
	}

	[Test]
	public void InstanceMembers() {
		List<string> labels = GetLabels("GameState.player.|", false, TEST_GAME);
		Assert.That(labels, Is.SupersetOf(new[] { "name", "inventory", "GetHealthPercent()", "Add(int a, int b)", "Add(float a, float b)" }));
		Assert.That(labels, Does.Not.Contain("Instance")); // static
	}

	[Test]
	public void PrefixIsReplaced() {
		List<AutoCompleteOption> options = GetOptions("GameState.player.na|", false, TEST_GAME);
		Assert.That(options[0].label, Is.EqualTo("name"));
		Assert.That(options[0].insertText, Is.EqualTo("name"));
	}

	[Test]
	public void MethodsInsertParentheses() {
		AutoCompleteOption option = GetOptions("GameState.player.ADD|", false, TEST_GAME).First(o => o.label == "Add(int a, int b)");
		Assert.That(option.insertText, Is.EqualTo("Add()"));
		Assert.That(option.cursorOffset, Is.EqualTo(-1)); // inside the parentheses
	}

	[Test]
	public void SubstringMatches() {
		Assert.That(GetLabels("GameState.player.health|", false, TEST_GAME), Is.SupersetOf(new[] { "currentHealth", "maxHealth" }));
	}

	[Test]
	public void CompletelyTypedNamesAreStillSuggested() {
		Assert.That(GetLabels("GameState.player.name|", false, TEST_GAME), Does.Contain("name"));
		Assert.That(GetOptions("GameState|").First(o => o.label == "GameState").requiredUsing, Is.EqualTo(TEST_GAME)); // so accepting it adds the using
	}

	[TestCase("GameState.player.GetItem(1).|")]
	[TestCase("1 + GameState.player.weapon.|")]
	[TestCase("item.|")] // variables have a declared type
	[TestCase("((Item)player.weapon).|")]
	[TestCase("new Item(\"a\").|")]
	[TestCase("Generics.Default<Item>().|")] // generic method calls
	[TestCase("[player.weapon, item][0].|")] // elements of collections
	[TestCase("player.Add(1, 2); player.weapon.|")] // statements of actions
	[TestCase("zoo.animals[1 + 1].Bark().Length + player.weapon.|")]
	public void MembersOfChains(string input) {
		Assert.That(GetLabels(input, false, TEST_GAME), Does.Contain("name"));
	}

	[TestCase("Math.Max(GameState.player.|")]
	[TestCase("Math.Max(GameState.player.cur|, 2)")]
	public void MembersInsideUnfinishedArguments(string input) {
		Assert.That(GetLabels(input, false, "System", TEST_GAME), Does.Contain("currentHealth"));
	}

	[Test]
	public void StaticMembers() {
		Assert.That(GetLabels("Math.|", false, "System"), Is.SupersetOf(new[] { "PI", "Max(int val1, int val2)" }));
		List<string> labels = GetLabels("Player.|", false, TEST_GAME);
		Assert.That(labels, Is.SupersetOf(new[] { "Instance", "StaticCount", "Nested" }));
		Assert.That(labels, Does.Not.Contain("name"));
		Assert.That(GetLabels("int.|"), Does.Contain("MaxValue"));
	}

	[Test]
	public void MembersOfGenericTypes() {
		Assert.That(GetLabels("Comparer<int>.|", false, "System.Collections.Generic"), Does.Contain("Default"));
		Assert.That(GetLabels("((List<int>)zoo.boxed).|", false, "System.Collections.Generic"), Does.Contain("Count"));
	}

	[Test]
	public void Namespaces() {
		Assert.That(GetOptions("Sys|").Any(o => o.label == "System" && o.detail == "namespace"));
		List<string> labels = GetLabels("System.Collections.|");
		Assert.That(labels, Is.SupersetOf(new[] { "Generic", "ArrayList" }));
		AutoCompleteOption list = GetOptions("System.Collections.Generic.Li|").First(o => o.label == "List<T>");
		Assert.That(list.insertText, Is.EqualTo("List<>"));
		Assert.That(list.cursorOffset, Is.EqualTo(-1)); // inside the brackets
		Assert.That(list.requiredUsing, Is.Null); // fully qualified
	}

	[Test]
	public void GenericTypes() {
		AutoCompleteOption dictionary = GetOptions("Dictio|").First(o => o.label == "Dictionary<TKey, TValue>");
		Assert.That(dictionary.insertText, Is.EqualTo("Dictionary<>"));
		Assert.That(dictionary.requiredUsing, Is.EqualTo("System.Collections.Generic"));
	}

	[TestCase("List<Ga|")]
	[TestCase("Dictionary<string, Ga|")]
	[TestCase("(Ga|")] // a cast
	[TestCase("zoo is Ga|")]
	public void TypesInTypePositions(string input) {
		Assert.That(GetLabels(input, false, TEST_GAME, "System.Collections.Generic"), Does.Contain("GameState"));
	}

	[Test]
	public void OnlyTypesAfterIsAndAs() {
		List<string> labels = GetLabels("zoo as |", true, TEST_GAME);
		Assert.That(labels, Is.SupersetOf(new[] { "Dog", "int" }));
		Assert.That(labels, Does.Not.Contain("zoo"));
		Assert.That(labels, Does.Not.Contain("true"));
	}

	[Test]
	public void TypeKeywords() {
		Assert.That(GetLabels("in|"), Does.Contain("int"));
	}

	[Test]
	public void TypesNeedAPrefix() {
		Assert.That(GetLabels("|", false, TEST_GAME), Is.EquivalentTo(EvaluationTests.VARIABLES.Keys)); // only the variables
		Assert.That(GetOptions("GameSt|", false, TEST_GAME)[0].insertText, Is.EqualTo("GameState"));
	}

	[Test]
	public void ExplicitRequestListsImportedTypes() {
		List<string> labels = GetLabels("|", true, TEST_GAME);
		Assert.That(labels, Is.SupersetOf(new[] { "GameState", "Player", "zoo" }));
		Assert.That(labels, Does.Not.Contain("Math")); // System is not imported
	}

	[Test]
	public void TypesFromOtherNamespacesRequireAUsing() {
		Assert.That(GetOptions("GameSt|").First(o => o.label == "GameState").requiredUsing, Is.EqualTo(TEST_GAME));
		Assert.That(GetOptions("GameSt|", false, TEST_GAME).First(o => o.label == "GameState").requiredUsing, Is.Null);
	}

	[Test]
	public void ConflictingTypesRequireAnAlias() {
		AutoCompleteOption other = GetOptions("Playe|", false, TEST_GAME).First(o => o.label == "Player" && o.detail == "UIConstraints.Core.Tests.OtherGame");
		Assert.That(other.requiredUsing, Is.EqualTo("Player=UIConstraints.Core.Tests.OtherGame.Player"));
	}

	[Test]
	public void AliasesAndVariables() {
		Assert.That(GetLabels("|", true, "P2=UIConstraints.Core.Tests.OtherGame.Player"), Does.Contain("P2"));
		AutoCompleteOption item = GetOptions("it|")[0];
		Assert.That(item.label, Is.EqualTo("item"));
		Assert.That(item.detail, Is.EqualTo("Item")); // the type of the variable
	}

	[TestCase("GameState |")] // directly after another value
	[TestCase("\"Gam|")] // inside a string
	[TestCase("nope.|")] // unknown receiver
	public void NoSuggestions(string input) {
		Assert.That(GetLabels(input, true, TEST_GAME), Is.Empty);
	}

	[Test]
	public void Keywords() {
		Assert.That(GetLabels("tr|"), Does.Contain("true"));
	}

	[Test]
	public void TypesAfterNew() {
		Assert.That(GetLabels("new It|", false, TEST_GAME), Does.Contain("Item"));
	}

	[Test]
	public void IndexedReceivers() {
		Assert.That(GetLabels("zoo.animals[0].|", false, TEST_GAME), Does.Contain("Name"));
		Assert.That(GetLabels("zoo.animals[player.level - 3].|", false, TEST_GAME), Does.Contain("Name"));
	}
}
}
