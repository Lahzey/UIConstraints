using System.Linq;
using NUnit.Framework;
using UIConstraints.Core.Parsing;

namespace UIConstraints.Core.Tests {
// everything has a known type while parsing, so mistakes are found before evaluating
public class TypeCheckTests {
	private const string TEST_GAME = EvaluationTests.TEST_GAME;

	private static ParseResult Parse(string code, params string[] usings) {
		return CodeParser.TryParse(code, new ParseContext(usings, EvaluationTests.VARIABLES));
	}

	[TestCase("1 + true", "Operator '+' cannot be applied to operands of type int and bool")]
	[TestCase("1 < true", "Operator '<' cannot be applied to operands of type int and bool")]
	[TestCase("1 && true", "Operator '&&' cannot be applied to operands of type int and bool")]
	[TestCase("1 == \"1\"", "Operator '==' cannot be applied to operands of type int and string")]
	[TestCase("!5", "Operator '!' cannot be applied to an operand of type int")]
	[TestCase("1 ? 2 : 3", "The condition has to be a bool, not int")]
	[TestCase("true ? 1 : \"a\"", "The type of the conditional expression can't be determined, as there is no implicit conversion between int and string")]
	[TestCase("1 < 2 > 3", "Operator '>' cannot be applied to operands of type bool and int")]
	[TestCase("player.nope", "'Player' does not contain a member named 'nope'")]
	[TestCase("player.name.Length.Foo", "'int' does not contain a member named 'Foo'")]
	[TestCase("player.GetHealthPercent", "'GetHealthPercent' is a method, which needs parentheses to be called")]
	[TestCase("Player.name", "'name' is not static, it can only be accessed on an instance of Player")]
	[TestCase("player.Instance", "'Instance' is static, access it through the type 'Player' instead")]
	[TestCase("player.Missing()", "'Player' does not contain a method named 'Missing'")]
	[TestCase("player.Add(\"x\", 1)", "No overload of 'Add' takes the arguments (string, int)")]
	[TestCase("player.DoNothing()", "A method without return value (void) does not have a value")]
	[TestCase("player.DoNothing().x", "A method without return value (void) does not have a value")]
	[TestCase("(string)5", "Cannot convert int to string")]
	[TestCase("(Item)zoo", "Cannot convert Zoo to Item")]
	[TestCase("(zoo)player", "Only a type can be followed by a value (to cast it)")]
	[TestCase("5 as int", "'as' needs a type that can be null, but int is a value type (use a cast instead)")]
	[TestCase("zoo.pet as Item", "Animal can never be converted to Item with 'as'")]
	[TestCase("zoo is 5", "'5' is not a type")]
	[TestCase("Player", "'Player' is a type, which is not valid in the given context")]
	[TestCase("System", "'System' is a namespace, which is not valid in the given context")]
	[TestCase("System.Nope", "The type or namespace 'Nope' does not exist in the namespace 'System'")]
	[TestCase("System.Collections.List<int>", "The type or namespace 'List<>' does not exist in the namespace 'System.Collections'")]
	[TestCase("Player<int>", "The type 'Player<>' could not be found")]
	public void Errors(string code, string message) {
		ParseResult parseResult = Parse(code, TEST_GAME);
		Assert.That(parseResult.Success, Is.False);
		Assert.That(parseResult.Exception.GetMessage(0), Is.EqualTo(message));
	}

	[Test]
	public void AmbiguousCalls() {
		ParseResult parseResult = Parse("Overloads.Pick(1, 2)", TEST_GAME);
		Assert.That(parseResult.Exception.GetMessage(0), Does.StartWith("The call is ambiguous between Pick("));
	}

	// an error is reported once, not again by every expression containing it
	[TestCase("nope.a.b + 1")]
	[TestCase("(nope).a(1) * 2 == 3")]
	[TestCase("(Dog)nope")]
	public void ErrorsAreNotRepeated(string code) {
		ParseResult parseResult = Parse(code, TEST_GAME);
		Assert.That(parseResult.Exception.MessageCount(), Is.EqualTo(1), () => string.Join(" | ", Enumerable.Range(0, parseResult.Exception.MessageCount()).Select(i => parseResult.Exception.GetMessage(i))));
		Assert.That(parseResult.Exception.GetMessage(0), Is.EqualTo("Unknown identifier 'nope'"));
	}

	[Test]
	public void InvalidAliases() {
		ParseResult parseResult = Parse("P.x", "P=Nope.Type");
		Assert.That(parseResult.Exception.GetMessage(0), Does.StartWith("The alias 'P' refers to 'Nope.Type', which is not a valid type"));
	}

	[TestCase("List<int>", ParseTarget.Type, true)]
	[TestCase("Dictionary<string, int[]>[]", ParseTarget.Type, true)]
	[TestCase("int", ParseTarget.Type, true)]
	[TestCase("1 + 2", ParseTarget.Type, false)]
	[TestCase("System.Collections", ParseTarget.Type, false)]
	[TestCase("System.Collections", ParseTarget.Any, true)]
	[TestCase("List<int>", ParseTarget.Value, false)]
	public void ParseTargets(string code, ParseTarget target, bool success) {
		Assert.That(CodeParser.TryParse(code, new ParseContext(new[] { "System.Collections.Generic" }), target).Success, Is.EqualTo(success));
	}
}
}
