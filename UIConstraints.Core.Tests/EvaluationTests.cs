using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Tests.TestGame;

namespace UIConstraints.Core.Tests {
public class EvaluationTests {
	private const string TEST_GAME = "UIConstraints.Core.Tests.TestGame";

	private DataContext context;
	private Player player;

	[SetUp]
	public void SetUp() {
		context = new DataContext(null);
		player = new Player();
		context.SetLocal("player", player);
		context.SetLocal("showInventory", true);
		context.SetLocal("item", new Item("LoopItem"));
	}

	private object Evaluate(string code, params string[] usings) {
		ParseResult parseResult = CodeParser.TryParse(code, new ParseContext(usings));
		Assert.That(parseResult.Success, Is.True, () => $"'{code}' failed to parse: " + string.Join(" | ", Enumerable.Range(0, parseResult.Exception.MessageCount()).Select(i => parseResult.Exception.GetMessage(i))));
		return parseResult.Expression?.Evaluate(context);
	}

	// NUnit considers numbers of different types equal (e.g. 7L and 7), but the type matters for overload resolution
	private static void AssertResult(object actual, object expected) {
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
	[TestCase("1 == 1 && 2 == 2", true)]
	[TestCase("1 >= 1 && 2 <= 2", true)]
	[TestCase("showInventory", true)]
	[TestCase("!showInventory", false)]
	[TestCase("true ? 1 : 2", 1)]
	[TestCase("1 > 2 ? \"a\" : \"b\"", "b")]
	public void Logic(string code, object expected) {
		AssertResult(Evaluate(code), expected);
	}

	[TestCase("player.name", "Hero")]
	[TestCase("player.currentHealth / player.maxHealth", 0.75f)]
	[TestCase("player.weapon.name", "Sword")]
	[TestCase("item.name", "LoopItem")]
	[TestCase("player.inventory.Count", 0)]
	[TestCase("!showInventory || player.level > 2", true)]
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
	[TestCase("player.DoNothing()", null)]
	public void FunctionCalls(string code, object expected) {
		AssertResult(Evaluate(code), expected);
	}

	[TestCase("player.Throws()", "Method 'Throws' threw InvalidOperationException: boom")]
	[TestCase("player.Missing()", "No method 'Missing' with fitting parameters found for type 'UIConstraints.Core.Tests.TestGame.Player'")]
	[TestCase("GameState.player.name", "Trying to access 'player' of null")] // without the using, GameState is an (unset) context variable
	public void EvaluationErrors(string code, string message) {
		EvaluationException exception = Assert.Throws<EvaluationException>(() => Evaluate(code));
		Assert.That(exception.Message, Is.EqualTo(message));
	}

	[TestCase("player.Add(1,)")]
	[TestCase("player.Add(1 2)")]
	[TestCase("Foo(1)")] // there is no implicit 'this' to call methods on
	public void ParseErrors(string code) {
		Assert.That(CodeParser.TryParse(code).Success, Is.False);
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

	[Test]
	public void TypesAreNotValues() {
		EvaluationException exception = Assert.Throws<EvaluationException>(() => Evaluate("Player", TEST_GAME));
		Assert.That(exception.Message, Is.EqualTo("'Player' is a type and cannot be used as a value"));
	}

	[Test]
	public void ExampleSceneExpressions() {
		context.SetLocal("item", new KeyValuePair<Item, int>(new Item("Apple"), 4));
		AssertResult(Evaluate("item.Key.name"), "Apple");
		AssertResult(Evaluate("item.Value"), 4);
		Assert.That(Evaluate("player.inventory"), Is.SameAs(player.inventory));
		AssertResult(Evaluate("player.name.ToUpper()"), "HERO");
		Assert.That(Evaluate(""), Is.Null);
	}
}
}
