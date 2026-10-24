using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Tests.TestGame;

namespace UIConstraints.Core.Tests {
public class EvaluationTests {
	public const string TEST_GAME = "UIConstraints.Core.Tests.TestGame";
	public const string OTHER_GAME = "UIConstraints.Core.Tests.OtherGame";

	// what SetUp puts into the context
	public static readonly Dictionary<string, Type> VARIABLES = new Dictionary<string, Type> {
		{ "player", typeof(Player) },
		{ "showInventory", typeof(bool) },
		{ "item", typeof(Item) },
		{ "zoo", typeof(Zoo) }
	};

	private DataContext context;
	private Player player;

	[SetUp]
	public void SetUp() {
		context = new DataContext(null);
		player = new Player();
		context.SetLocal("player", player);
		context.SetLocal("showInventory", true);
		context.SetLocal("item", new Item("LoopItem"));
		context.SetLocal("zoo", new Zoo());
	}

	private object Evaluate(string code, params string[] usings) {
		return Evaluate(code, VARIABLES, usings);
	}

	private object Evaluate(string code, Dictionary<string, Type> variables, params string[] usings) {
		ParseResult parseResult = CodeParser.TryParse(code, new ParseContext(usings, variables));
		Assert.That(parseResult.Success, Is.True, () => $"'{code}' failed to parse: " + string.Join(" | ", Enumerable.Range(0, parseResult.Exception.MessageCount()).Select(i => parseResult.Exception.GetMessage(i))));
		return parseResult.Expression?.Evaluate(context);
	}

	// NUnit considers numbers of different types equal (e.g. 7L and 7), but the type matters for overload resolution
	public static void AssertResult(object actual, object expected) {
		Assert.That(actual, Is.EqualTo(expected));
		Assert.That(actual?.GetType(), Is.EqualTo(expected?.GetType()));
	}

	[TestCase("1 + 2 * 3", 7)]
	[TestCase("2 * 3 + 1", 7)]
	[TestCase("10 - 4 - 3", 3)]
	[TestCase("10 - 2 + 3", 11)]
	[TestCase("2 * (3 + 4)", 14)]
	[TestCase("10 - 2 * (1 + 2)", 4)]
	[TestCase("(1 + 2) * (3 + 4)", 21)]
	[TestCase("((2))", 2)]
	[TestCase("7 % 4", 3)]
	[TestCase("2 ^ 3", 8.0)]
	[TestCase("2 * 3 ^ 2", 18.0)]
	[TestCase("1 + 0.5", 1.5f)]
	[TestCase("0.25 * 4", 1f)]
	[TestCase("3 / 2", 1)]
	[TestCase("3 / 2.0", 1.5f)]
	[TestCase("5L + 1", 6L)]
	[TestCase("\"HP: \" + 5", "HP: 5")]
	[TestCase("  1 + 2  ", 3)]
	[TestCase("1 +\n2", 3)]
	[TestCase("(1) - 2", -1)] // a bracket around a value followed by an operator is not a cast
	public void Arithmetic(string code, object expected) {
		AssertResult(Evaluate(code), expected);
	}

	[TestCase("1 == 1.0", true)]
	[TestCase("1 < 2 == true", true)]
	[TestCase("!false && false", false)]
	[TestCase("!(false && false)", true)]
	[TestCase("true || false && false", true)]
	[TestCase("1 + 1 == 2 && 2 > 1", true)]
	[TestCase("1 != 2", true)]
	[TestCase("null == null", true)]
	[TestCase("item == null", false)]
	[TestCase("1 == 1 && 2 == 2", true)]
	[TestCase("1 >= 1 && 2 <= 2", true)]
	[TestCase("showInventory", true)]
	[TestCase("!showInventory", false)]
	[TestCase("true ? 1 : 2", 1)]
	[TestCase("true ? 1 : 2.5", 1f)] // converted to the common type
	[TestCase("1 > 2 ? \"a\" : \"b\"", "b")]
	[TestCase("false ? item : null", null)]
	public void Logic(string code, object expected) {
		AssertResult(Evaluate(code), expected);
	}

	[TestCase("player.name", "Hero")]
	[TestCase("player.currentHealth / player.maxHealth", 0.75f)]
	[TestCase("player.weapon.name", "Sword")]
	[TestCase("item.name", "LoopItem")]
	[TestCase("player.inventory.Count", 0)]
	[TestCase("!showInventory || player.level > 2", true)]
	[TestCase("zoo.named.Name", "dog")] // interface members
	[TestCase("zoo.named.ToString()", "UIConstraints.Core.Tests.TestGame.Dog")] // object members on interfaces
	public void MemberAccess(string code, object expected) {
		AssertResult(Evaluate(code), expected);
	}

	[TestCase("player.GetHealthPercent()", 0.75f)]
	[TestCase("player.GetHealthPercent( )", 0.75f)]
	[TestCase("player.Add(1, 2)", 3)]
	[TestCase("player.Add(1, 2.0)", 3.5f)]
	[TestCase("player.Add(1 + 1, 2 * 3)", 8)]
	[TestCase("player.Add( 1 , 2 )", 3)]
	[TestCase("player.Greet(\"Bob\")", "Hello Bob")]
	[TestCase("player.Greet(\"Bob\", \"Hi\")", "Hi Bob")]
	[TestCase("player.GetItem(3).name", "Item3")]
	[TestCase("player.GetItem(player.Add(1, 1)).name", "Item2")]
	[TestCase("player.GetHealthPercent() * 100", 75f)]
	[TestCase("10 - player.Add(1, 2) * (1 + 1)", 4)]
	[TestCase("player.name.ToUpper()", "HERO")]
	[TestCase("player.name.Substring(1, 2)", "er")]
	public void FunctionCalls(string code, object expected) {
		AssertResult(Evaluate(code), expected);
	}

	[TestCase("player.Throws()", "Method 'Throws' threw InvalidOperationException: boom")]
	[TestCase("(Dog)zoo.plainAnimal", "Cannot cast Animal to Dog")]
	[TestCase("(int)(object)zoo", "Cannot cast Zoo to int")]
	public void EvaluationErrors(string code, string message) {
		EvaluationException exception = Assert.Throws<EvaluationException>(() => Evaluate(code, TEST_GAME));
		Assert.That(exception.Message, Is.EqualTo(message));
	}

	[TestCase("player.Add(1,)")]
	[TestCase("player.Add(1 2)")]
	[TestCase("Foo(1)")] // there is no implicit 'this' to call methods on
	public void ParseErrors(string code) {
		Assert.That(CodeParser.TryParse(code, new ParseContext(null, VARIABLES)).Success, Is.False);
	}

	[TestCase("Math.Max(3, 7)", 7, "System")]
	[TestCase("Math.Max(3, 7.5)", 7.5f, "System")]
	[TestCase("Math.Abs(0 - 1)", 1, "System")]
	[TestCase("Math.PI > 3", true, "System")]
	[TestCase("String.Concat(\"a\", \"b\")", "ab", "System")]
	[TestCase("Player.Instance.name", "Static", TEST_GAME)]
	[TestCase("Player.StaticCount + 1", 43, TEST_GAME)]
	[TestCase("Player.Nested.Info", "nested", TEST_GAME)]
	[TestCase("GameState.player.GetHealthPercent()", 0.75f, TEST_GAME)]
	public void StaticAccess(string code, object expected, string usedNamespace) {
		AssertResult(Evaluate(code, usedNamespace), expected);
	}

	[TestCase("GameState.player.name", "Unknown identifier 'GameState'", "GameState")] // the namespace is not used
	[TestCase("Foo.bar + 1", "Unknown identifier 'Foo'", "Foo")]
	[TestCase("Player.Kind", "'Player' is ambiguous between UIConstraints.Core.Tests.TestGame.Player and UIConstraints.Core.Tests.OtherGame.Player", "Player", TEST_GAME, OTHER_GAME)]
	[TestCase("Lisst<int>", "The type 'Lisst<>' could not be found", "Lisst`1")]
	public void UnresolvedIdentifiers(string code, string message, string identifier, params string[] usings) {
		ParseResult parseResult = CodeParser.TryParse(code, new ParseContext(usings, VARIABLES));
		Assert.That(parseResult.Success, Is.False);
		Assert.That(parseResult.Exception.GetMessage(0), Is.EqualTo(message));
		Assert.That(parseResult.Exception.GetUnresolvedIdentifier(0), Is.EqualTo(identifier));
		string name = TypeNames.GetBaseName(identifier);
		Assert.That(parseResult.Exception.GetStartPosition(0), Is.EqualTo(code.IndexOf(name, StringComparison.Ordinal)));
		Assert.That(parseResult.Exception.GetEndPosition(0), Is.EqualTo(code.IndexOf(name, StringComparison.Ordinal) + name.Length));
	}

	[Test]
	public void VariablesHideTypes() {
		context.SetLocal("Player", "variable");
		AssertResult(Evaluate("Player", new Dictionary<string, Type> { { "Player", typeof(string) } }, TEST_GAME), "variable");
	}

	[Test]
	public void VariablesMustMatchTheirDeclaredType() {
		context.SetLocal("item", "not an item");
		EvaluationException exception = Assert.Throws<EvaluationException>(() => Evaluate("item.name"));
		Assert.That(exception.Message, Is.EqualTo("The variable 'item' is declared as Item, but its value is string"));

		exception = Assert.Throws<EvaluationException>(() => Evaluate("missing", new Dictionary<string, Type> { { "missing", typeof(int) } }));
		Assert.That(exception.Message, Is.EqualTo("The variable 'missing' is not available here"));
	}

	[TestCase("Player.Kind", "other", TEST_GAME, OTHER_GAME, "Player=UIConstraints.Core.Tests.OtherGame.Player")] // alias resolves the ambiguity
	[TestCase("P2.Kind + Player.StaticCount", "other42", TEST_GAME, "P2=UIConstraints.Core.Tests.OtherGame.Player")]
	[TestCase("Nested.Info", "nested", "Nested=UIConstraints.Core.Tests.TestGame.Player.Nested")] // nested types use '.' like in C#
	[TestCase("((IntList)zoo.boxed).Count", 3, "IntList=System.Collections.Generic.List<int>")]
	[TestCase("zoo.groups is Groups", true, "Groups=System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>")]
	public void Aliases(string code, object expected, params string[] usings) {
		AssertResult(Evaluate(code, usings), expected);
	}

	[Test]
	public void SuggestedUsings() {
		Assert.That(TypeResolver.SuggestUsings("GameState", new string[0]), Is.EqualTo(new[] { TEST_GAME }));
		Assert.That(TypeResolver.SuggestUsings("Player", new string[0]), Is.SupersetOf(new[] { TEST_GAME, OTHER_GAME }));
		Assert.That(TypeResolver.SuggestUsings("Player", new[] { TEST_GAME, OTHER_GAME }), Is.EqualTo(new[] { "Player=UIConstraints.Core.Tests.TestGame.Player", "Player=UIConstraints.Core.Tests.OtherGame.Player" }));
		Assert.That(TypeResolver.SuggestUsings("List`1", new string[0]), Does.Contain("System.Collections.Generic"));
		Assert.That(TypeResolver.SuggestUsings("DoesNotExist", new string[0]), Is.Empty);
	}

	[Test]
	public void ExampleSceneExpressions() {
		Dictionary<string, Type> loopVariables = new Dictionary<string, Type>(VARIABLES) { ["item"] = typeof(KeyValuePair<Item, int>) };
		context.SetLocal("item", new KeyValuePair<Item, int>(new Item("Apple"), 4));
		AssertResult(Evaluate("item.Key.name", loopVariables), "Apple");
		AssertResult(Evaluate("item.Value", loopVariables), 4);
		Assert.That(Evaluate("player.inventory"), Is.SameAs(player.inventory));
		AssertResult(Evaluate("player.name.ToUpper()"), "HERO");
		Assert.That(Evaluate(""), Is.Null);
	}
}
}
