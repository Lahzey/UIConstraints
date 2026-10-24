using System.Linq;
using NUnit.Framework;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Tests.TestGame;

namespace UIConstraints.Core.Tests {
// casts, type checks, generic and array types, namespaces and overload resolution
public class TypeTests {
	private const string TEST_GAME = EvaluationTests.TEST_GAME;
	private const string GENERIC_COLLECTIONS = "System.Collections.Generic";

	private DataContext context;

	[SetUp]
	public void SetUp() {
		context = new DataContext(null);
		context.SetLocal("zoo", new Zoo());
		context.SetLocal("player", new Player());
	}

	private object Evaluate(string code, params string[] usings) {
		ParseResult parseResult = CodeParser.TryParse(code, new ParseContext(usings, EvaluationTests.VARIABLES));
		Assert.That(parseResult.Success, Is.True, () => $"'{code}' failed to parse: " + string.Join(" | ", Enumerable.Range(0, parseResult.Exception.MessageCount()).Select(i => parseResult.Exception.GetMessage(i))));
		return parseResult.Expression.Evaluate(context);
	}

	[TestCase("(int)zoo.health", 75)] // truncated like in C#
	[TestCase("(int)zoo.health * 2", 150)] // the cast binds tighter than *
	[TestCase("(float)(int)zoo.health", 75f)] // chained casts are applied right to left
	[TestCase("(long)5", 5L)]
	[TestCase("(char)65", 'A')]
	[TestCase("(object)1", 1)]
	[TestCase("((Dog)zoo.pet).Bark()", "woof")]
	[TestCase("((Dog)zoo.named).Bark()", "woof")] // from an interface
	[TestCase("((List<int>)zoo.boxed).Count", 3, GENERIC_COLLECTIONS)]
	[TestCase("(int)zoo.money", 250)] // user-defined explicit conversion
	[TestCase("!(bool)(object)true", false)]
	[TestCase("((UIConstraints.Core.Tests.TestGame.Dog)zoo.pet).Name", "dog")] // fully qualified
	public void Casts(string code, object expected, params string[] usings) {
		EvaluationTests.AssertResult(Evaluate(code, usings.Append(TEST_GAME).ToArray()), expected);
	}

	[TestCase("zoo.pet is Dog", true)]
	[TestCase("zoo.plainAnimal is Dog", false)]
	[TestCase("zoo.pet is INamed", true)]
	[TestCase("zoo.boxed is List<int>", true)]
	[TestCase("zoo.boxed is List<string>", false)]
	[TestCase("zoo.groups is Dictionary<string, List<int>>", true)]
	[TestCase("zoo.animals is Animal[]", true)]
	[TestCase("zoo.boxed is int[][]", false)]
	[TestCase("(zoo.pet as Dog).Bark()", "woof")]
	[TestCase("zoo.plainAnimal as Dog == null", true)]
	[TestCase("zoo.boxed as List<int> != null && zoo.pet is Dog", true)]
	public void IsAndAs(string code, object expected) {
		EvaluationTests.AssertResult(Evaluate(code, TEST_GAME, GENERIC_COLLECTIONS), expected);
	}

	[TestCase("Comparer<int>.Default.Compare(1, 2)", -1, GENERIC_COLLECTIONS)]
	[TestCase("EqualityComparer<string>.Default.Equals(\"a\", \"a\")", true, GENERIC_COLLECTIONS)]
	[TestCase("System.Math.Max(1, 2)", 2)]
	[TestCase("System.Collections.Generic.Comparer<int>.Default.Compare(2, 1)", 1)]
	[TestCase("int.MaxValue", int.MaxValue)]
	[TestCase("int.Parse(\"42\")", 42)]
	[TestCase("string.Concat(\"a\", \"b\")", "ab")]
	public void TypesAndNamespaces(string code, object expected, params string[] usings) {
		EvaluationTests.AssertResult(Evaluate(code, usings), expected);
	}

	// generics and comparisons both use < and >, they are told apart like in C#
	[TestCase("Overloads.Both(1 < 2, 3 > 2)", true)]
	[TestCase("1 < 2 == true", true)]
	public void ComparisonsAreNotGenerics(string code, object expected) {
		EvaluationTests.AssertResult(Evaluate(code, TEST_GAME), expected);
	}

	[TestCase("Overloads.Widen(1)", "long")] // the better conversion target wins
	[TestCase("Overloads.Describe(null)", "string")] // the more specific type wins
	[TestCase("Overloads.Describe(1)", "object")]
	[TestCase("Overloads.Sum(1, 2, 3)", 6)] // params array
	[TestCase("Overloads.Sum()", 0)]
	[TestCase("Overloads.Cents(5)", 5)] // user-defined implicit conversion
	public void Overloads(string code, object expected) {
		EvaluationTests.AssertResult(Evaluate(code, TEST_GAME), expected);
	}
}
}
