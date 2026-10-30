using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Tests.TestGame;

namespace UIConstraints.Core.Tests {
// generic methods, collections, arrays, nullable types and the numeric types beyond int, long, float and double
public class SyntaxTests {
	private static readonly Dictionary<string, Type> VARIABLES = new Dictionary<string, Type> {
		{ "counter", typeof(Counter) },
		{ "player", typeof(Player) }
	};

	private static readonly string[] USINGS = { EvaluationTests.TEST_GAME, "System.Collections.Generic", "System.Linq" };

	private DataContext context;

	[SetUp]
	public void SetUp() {
		context = new DataContext(null);
		context.SetLocal("counter", new Counter());
		context.SetLocal("player", new Player());
	}

	private static ParseResult Parse(string code) {
		return CodeParser.TryParse(code, new ParseContext(USINGS, VARIABLES));
	}

	private static string Messages(ParseResult parseResult) {
		return parseResult.Exception == null ? "" : string.Join(" | ", Enumerable.Range(0, parseResult.Exception.MessageCount()).Select(i => parseResult.Exception.GetMessage(i)));
	}

	private object Evaluate(string code) {
		ParseResult parseResult = Parse(code);
		Assert.That(parseResult.Success, Is.True, () => $"'{code}' failed to parse: " + Messages(parseResult));
		return parseResult.Expression.Evaluate(context);
	}

	[TestCase("counter.Get<Item>().name", "boxed")]
	[TestCase("counter.Get<Dog>() == null", true)]
	[TestCase("counter.Get<UIConstraints.Core.Tests.TestGame.Item>().name.Length", 5)]
	[TestCase("Generics.Default<int>()", 0)]
	[TestCase("Generics.Default<List<int>>() == null", true)]
	[TestCase("Generics.Pick(1, 2.5f)", 2.5f)] // inferred as float, which both arguments convert to
	[TestCase("Generics.Pick(\"a\", \"b\")", "b")]
	[TestCase("Generics.ListOf(1, 2, 3).Count", 3)] // inferred from the elements of the params array
	[TestCase("Generics.First(counter.list)", 1)] // inferred from IEnumerable<int>, which List<int> implements
	[TestCase("Generics.First([\"x\", \"y\"])", "x")]
	[TestCase("Enumerable.Max(counter.list)", 3)] // the non-generic overload is preferred over the generic one
	[TestCase("Enumerable.Contains(counter.list, 2)", true)]
	[TestCase("Enumerable.Count(counter.scores)", 1)]
	public void GenericMethods(string code, object expected) {
		EvaluationTests.AssertResult(Evaluate(code), expected);
	}

	[TestCase("[\"Test1\", \"Test2\"].Length", 2)]
	[TestCase("[\"Test1\", \"Test2\"][1]", "Test2")]
	[TestCase("[1, 2.5f][0]", 1f)] // the element type is the one all elements convert to
	[TestCase("[player.weapon, null][0].name", "Sword")]
	[TestCase("[[1, 2], [3]][0].Length", 2)]
	[TestCase("(true ? [1] : [2, 3]).Length", 1)]
	[TestCase("new List<string>([\"a\", \"b\"]).Count", 2)]
	[TestCase("new int[3].Length", 3)]
	[TestCase("new int[counter.list.Count + 1][3]", 0)]
	[TestCase("new string[2, 3].Length", 6)]
	[TestCase("new int[2, 3][1, 2]", 0)]
	[TestCase("new int[2, 3].Rank", 2)]
	[TestCase("new int?[2][0] == null", true)]
	public void CollectionsAndArrays(string code, object expected) {
		EvaluationTests.AssertResult(Evaluate(code), expected);
	}

	[TestCase("(int?)5", 5)]
	[TestCase("(int?)null == null", true)]
	[TestCase("((int?)null).HasValue", false)]
	[TestCase("new List<int?>().Count", 0)]
	[TestCase("player.weapon is Item?", true)] // for reference types, ? is just the type itself
	[TestCase("(System.Nullable<int>)3", 3)]
	[TestCase("((int?)null).GetValueOrDefault()", 0)]
	[TestCase("((int?)4).GetValueOrDefault(9)", 4)]
	[TestCase("((int?)null).GetValueOrDefault(9)", 9)]
	[TestCase("((int?)4).ToString()", "4")]
	[TestCase("((int?)7).Value", 7)]
	public void NullableTypes(string code, object expected) {
		EvaluationTests.AssertResult(Evaluate(code), expected);
	}

	[Test]
	public void ArrayTypes() {
		Assert.That(CodeParser.TryParse("int[,]", null, ParseTarget.Type).Expression.GetReferencedType(), Is.EqualTo(typeof(int[,])));
		Assert.That(CodeParser.TryParse("string[,,]", null, ParseTarget.Type).Expression.GetReferencedType(), Is.EqualTo(typeof(string[,,])));
		Assert.That(CodeParser.TryParse("int?[]", null, ParseTarget.Type).Expression.GetReferencedType(), Is.EqualTo(typeof(int?[])));
		Assert.That(CodeParser.TryParse("List<int?>", new ParseContext(USINGS), ParseTarget.Type).Expression.GetReferencedType(), Is.EqualTo(typeof(List<int?>)));
	}

	[Test]
	public void MultidimensionalElementsCanBeAssigned() {
		ParseResult parseResult = CodeParser.TryParse("counter.grid[1, 2] = 5", new ParseContext(USINGS, VARIABLES), ParseTarget.Action);
		Assert.That(parseResult.Success, Is.True, () => Messages(parseResult));
		Counter counter = (Counter)context.Get("counter");
		counter.grid = new int[2, 3];
		parseResult.Expression.Evaluate(context);
		Assert.That(counter.grid[1, 2], Is.EqualTo(5));
	}

	[TestCase("5u + 1", 6u)] // the literal takes the type of the unsigned operand
	[TestCase("5u + 1u", 6u)]
	[TestCase("5u + -6", -1L)] // uint and int are calculated as long (unless the int is a literal that fits uint, like in 5u - 6, which is uint like in C#)
	[TestCase("5ul * 2", 10ul)]
	[TestCase("18446744073709551615", ulong.MaxValue)] // too big for long
	[TestCase("3000000000", 3000000000L)]
	[TestCase("-5u", -5L)]
	[TestCase("1.5m + 1", 2.5)] // NUnit attributes can't hold decimals, see Decimals
	[TestCase("2 ^ 3", 8.0)]
	public void NumericTypes(string code, object expected) {
		object result = Evaluate(code);
		if (result is decimal value) Assert.That((double)value, Is.EqualTo(expected));
		else EvaluationTests.AssertResult(result, expected);
	}

	[Test]
	public void Decimals() {
		Assert.That(Evaluate("10m / 4"), Is.EqualTo(2.5m));
		Assert.That(Evaluate("0.1m + 0.2m"), Is.EqualTo(0.3m)); // exact, unlike with doubles
		Assert.That(Evaluate("-1.5m"), Is.EqualTo(-1.5m));
		Assert.That(Evaluate("1m + 5u"), Is.EqualTo(6m));
	}

	[TestCase("5ul + -1", "Operator '+' cannot be applied to operands of type ulong and int")]
	[TestCase("1.5m + 1.5", "Operator '+' cannot be applied to operands of type decimal and float")]
	[TestCase("-5ul", "Operator '-' cannot be applied to an operand of type ulong")]
	[TestCase("2m ^ 2", "Operator '^' cannot be applied to operands of type decimal and int")]
	[TestCase("5x", "'5x' is not a valid number")]
	[TestCase("1.5u", "'1.5u' is not a valid number")]
	[TestCase("[]", "An empty collection has no element type, use e.g. new int[0] instead")]
	[TestCase("[null, null]", "The element type can't be determined, as all elements are null")]
	[TestCase("[1, \"a\"]", "The elements have no common type (int, string), cast one of them to the type of the collection")]
	[TestCase("counter.Get<int>()", "No overload of 'Get<int>' takes the arguments ()")] // int does not fulfill the class constraint
	[TestCase("counter.Missing<int>()", "'Counter' does not contain a generic method named 'Missing' with 1 type argument")]
	[TestCase("counter.Get<Nope>()", "Unknown identifier 'Nope'")]
	[TestCase("Generics.Default()", "No overload of 'Default' takes the arguments ()")] // the type argument can't be inferred
	[TestCase("new int[\"a\"]", "The length of an array has to be an int, not string")]
	[TestCase("new int[2, 3][1]", "int[,] is indexed with 2 indices, not 1")]
	[TestCase("counter.array[1, 2]", "int[] is indexed with 1 index, not 2")]
	public void Errors(string code, string message) {
		ParseResult parseResult = Parse(code);
		Assert.That(parseResult.Success, Is.False, code);
		Assert.That(Messages(parseResult), Is.EqualTo(message));
	}

	[Test]
	public void ConditionalsAreNotNullableTypes() {
		EvaluationTests.AssertResult(Evaluate("counter.count == 0 ? [1, 2][1] : 3"), 2);
		EvaluationTests.AssertResult(Evaluate("counter.count == 0 ? (1) : 2"), 1);
	}
}
}
