using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Tests.TestGame;

namespace UIConstraints.Core.Tests {
// actions (statements that change something, e.g. when a button is clicked) and the expressions added for them: assignments, object creation, indexers, negation and operators of other types
public class ActionTests {
	private static readonly Dictionary<string, Type> VARIABLES = new Dictionary<string, Type> {
		{ "counter", typeof(Counter) },
		{ "value", typeof(int) }
	};

	private DataContext context;
	private Counter counter;

	[SetUp]
	public void SetUp() {
		context = new DataContext(null);
		counter = new Counter();
		Counter.total = 0;
		context.SetLocal("counter", counter);
		context.SetLocal("value", 5);
	}

	private static ParseResult Parse(string code, ParseTarget target) {
		return CodeParser.TryParse(code, new ParseContext(new[] { EvaluationTests.TEST_GAME, "System.Collections.Generic" }, VARIABLES), target);
	}

	private static string Messages(ParseResult parseResult) {
		return parseResult.Exception == null ? "" : string.Join(" | ", Enumerable.Range(0, parseResult.Exception.MessageCount()).Select(i => parseResult.Exception.GetMessage(i)));
	}

	private object Run(string code, ParseTarget target = ParseTarget.Action) {
		ParseResult parseResult = Parse(code, target);
		Assert.That(parseResult.Success, Is.True, () => $"'{code}' failed to parse: " + Messages(parseResult));
		return parseResult.Expression.Evaluate(context);
	}

	[Test]
	public void CallsVoidMethods() {
		Run("counter.Increment()");
		Assert.That(counter.count, Is.EqualTo(1));
	}

	[Test]
	public void RunsStatementsInOrder() {
		Run("counter.Add(2); counter.count = counter.count * 10; counter.Increment();");
		Assert.That(counter.count, Is.EqualTo(21));
	}

	[Test]
	public void Assignments() {
		Run("counter.count = value + 1; counter.text = \"x\" + counter.count; Counter.total = 7");
		Assert.That(counter.count, Is.EqualTo(6));
		Assert.That(counter.text, Is.EqualTo("x6"));
		Assert.That(Counter.total, Is.EqualTo(7));
	}

	[Test]
	public void ChainedAssignmentsAreRightAssociative() {
		Run("counter.count = Counter.total = 3");
		Assert.That(counter.count, Is.EqualTo(3));
		Assert.That(Counter.total, Is.EqualTo(3));
	}

	[Test]
	public void CompoundAssignments() {
		Run("counter.count += 5; counter.count -= 1; counter.count *= 3; counter.count /= 2; counter.text += \"a\"; counter.small += 1");
		Assert.That(counter.count, Is.EqualTo(6));
		Assert.That(counter.text, Is.EqualTo("a"));
		Assert.That(counter.small, Is.EqualTo(1));
	}

	[Test]
	public void ElementAssignments() {
		Run("counter.list[0] = 10; counter.array[1] += 5; counter.scores[\"b\"] = 2; counter.scores[\"a\"] += 1");
		Assert.That(counter.list[0], Is.EqualTo(10));
		Assert.That(counter.array[1], Is.EqualTo(7));
		Assert.That(counter.scores["b"], Is.EqualTo(2));
		Assert.That(counter.scores["a"], Is.EqualTo(2));
	}

	[Test]
	public void StructsAreAssignedAsAWhole() {
		Run("counter.position = new Vec(1, 2) * 2 + new Vec(0.5f, 0)");
		Assert.That(counter.position.x, Is.EqualTo(2.5f));
		Assert.That(counter.position.y, Is.EqualTo(4f));
	}

	[TestCase("counter.list[1]", 2)]
	[TestCase("counter.list[counter.list.Count - 1]", 3)]
	[TestCase("counter.array[2]", 3)]
	[TestCase("counter.scores[\"a\"]", 1)]
	[TestCase("counter.readOnlyList[0]", 1)]
	[TestCase("\"abc\"[1]", 'b')]
	[TestCase("1 + counter.list[1 + 1] * 2", 7)]
	[TestCase("new List<int>(counter.list)[0]", 1)]
	public void Indexers(string code, object expected) {
		EvaluationTests.AssertResult(Run(code, ParseTarget.Value), expected);
	}

	[TestCase("-value", -5)]
	[TestCase("- value", -5)]
	[TestCase("2 - -value", 7)]
	[TestCase("-2 * 3", -6)]
	[TestCase("3 * -2", -6)]
	[TestCase("-0.5", -0.5f)]
	[TestCase("1 - 2", -1)]
	[TestCase("-counter.list[0]", -1)]
	[TestCase("(-value).ToString()", "-5")]
	[TestCase("-(1 + 2)", -3)]
	[TestCase("true ? -1 : 1", -1)]
	[TestCase("counter.list.IndexOf(-1)", -1)]
	public void Negation(string code, object expected) {
		EvaluationTests.AssertResult(Run(code, ParseTarget.Value), expected);
	}

	[TestCase("new Vec(1, 2).x", 1f)]
	[TestCase("new Vec().y", 0f)]
	[TestCase("new Item(\"a\").name", "a")]
	[TestCase("new UIConstraints.Core.Tests.TestGame.Item(\"b\").name", "b")]
	[TestCase("new List<int>().Count", 0)]
	[TestCase("new List<int>(10).Capacity", 10)]
	[TestCase("new Dictionary<string, int>().Count", 0)]
	[TestCase("(new Vec(1, 2) * 3).y", 6f)]
	[TestCase("(-new Vec(1, 2)).x", -1f)]
	[TestCase("(new Vec(1, 2) + new Vec(3, 4)).ToString()", "(4, 6)")]
	public void ObjectCreationAndOperators(string code, object expected) {
		EvaluationTests.AssertResult(Run(code, ParseTarget.Value), expected);
	}

	[TestCase("counter.count = 1", ParseTarget.Value, "Assignments are only allowed in actions")]
	[TestCase("counter.Increment(); counter.Increment()", ParseTarget.Value, "Only actions can have multiple statements (separated by ';')")]
	[TestCase("counter.count == 1", ParseTarget.Action, "Only method calls, assignments and object creations can be used as statements")]
	[TestCase("counter.Increment(); 1", ParseTarget.Action, "Only method calls, assignments and object creations can be used as statements")]
	[TestCase("Counter", ParseTarget.Action, "'Counter' is a type, which is not valid in the given context")]
	[TestCase("counter.fixedValue = 2", ParseTarget.Action, "'fixedValue' is read only")]
	[TestCase("counter.Label = \"a\"", ParseTarget.Action, "'Label' is read only")]
	[TestCase("counter.list.Count = 2", ParseTarget.Action, "'Count' is read only")]
	[TestCase("counter.position.x = 1", ParseTarget.Action, "Cannot modify 'x', as it is a member of a Vec value, which would only change a copy. Assign a whole new value instead")]
	[TestCase("value = 1", ParseTarget.Action, "The variable 'value' can't be assigned, only fields, properties and elements")]
	[TestCase("counter.count = \"a\"", ParseTarget.Action, "Cannot assign string to int")]
	[TestCase("counter.count += 0.5", ParseTarget.Action, "Cannot assign the result (float) to int")]
	[TestCase("counter.readOnlyList[0] = 1", ParseTarget.Action, "The elements of IReadOnlyList<int> are read only")]
	[TestCase("counter.list[\"a\"]", ParseTarget.Value, "No overload of 'get_Item' takes the arguments (string)")]
	[TestCase("counter.array[\"a\"]", ParseTarget.Value, "Arrays are indexed with int, not string")]
	[TestCase("counter.count[0]", ParseTarget.Value, "int can't be indexed")]
	[TestCase("new INamed()", ParseTarget.Value, "Cannot create an instance of INamed, as it is an interface")]
	[TestCase("new Item(1)", ParseTarget.Value, "No overload of the constructor of Item takes the arguments (int)")]
	[TestCase("new counter()", ParseTarget.Value, "'counter' is not a type")]
	[TestCase("-\"a\"", ParseTarget.Value, "Operator '-' cannot be applied to an operand of type string")]
	public void Errors(string code, ParseTarget target, string message) {
		ParseResult parseResult = Parse(code, target);
		Assert.That(parseResult.Success, Is.False, code);
		Assert.That(Messages(parseResult), Is.EqualTo(message));
	}
}
}
