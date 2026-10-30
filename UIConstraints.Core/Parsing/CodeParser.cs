using System;
using System.Collections.Generic;
using System.Linq;
using UIConstraints.Core.Parsing.Expressions;

namespace UIConstraints.Core.Parsing {
// what the parsed source has to be
public enum ParseTarget {
	Value, // an expression that can be evaluated
	Type, // a type, e.g. List<int>
	Action, // statements separated by ';' that are executed for their effect (e.g. when a button is clicked): method calls and assignments, which nothing else may contain
	Any // anything, including namespaces (e.g. for code completion)
}

public static class CodeParser {
	private static readonly object INIT_LOCK = new object();

	private static List<List<CompiledPattern>> patternsByPriority; // highest priority first

	private static void Init() {
		IEnumerable<Type> subclasses =
			from assembly in AppDomain.CurrentDomain.GetAssemblies()
			from type in assembly.GetTypes()
			where type.IsSubclassOf(typeof(Expression)) && !type.IsAbstract
			select type;
		Dictionary<string, Type> patternTypes = new Dictionary<string, Type>();
		List<CompiledPattern> patterns = new List<CompiledPattern>();
		foreach (Type type in subclasses) {
			// get priority of the patterns from PatternPriority attribute (or 0 if not specified)
			int priority = 0;
			type.GetCustomAttributes(typeof(PatternPriorityAttribute), false).ToList().ForEach(a => priority = ((PatternPriorityAttribute)a).priority);

			// get ValuePattern attribute from type
			foreach (ExpressionPatternAttribute patternAttribute in type.GetCustomAttributes(typeof(ExpressionPatternAttribute), false)) {
				string pattern = patternAttribute.GetPattern();
				if (patternTypes.ContainsKey(pattern))
					throw new ArgumentException($"Duplicate use of pattern {pattern} by {type.Name} and {patternTypes[pattern].Name}.");
				patternTypes.Add(pattern, type);
				patterns.Add(new CompiledPattern(type, pattern, priority));
			}
		}

		patternsByPriority = patterns.GroupBy(p => p.priority).OrderByDescending(g => g.Key).Select(g => g.ToList()).ToList();
	}

	// resultType: for ParseTarget.Value, the type the value has to be convertible to (e.g. bool for a condition), null for any type
	public static ParseResult TryParse(string valueString, ParseContext context = null, ParseTarget target = ParseTarget.Value, Type resultType = null) {
		lock (INIT_LOCK) {
			if (patternsByPriority == null) Init();
		}

		ParseResult parseResult = Tokenizer.Tokenize(valueString, context ?? ParseContext.EMPTY);

		bool matched;
		do {
			matched = false;
			foreach ((int start, int end) in GetMatchRanges(parseResult))
				if (PatternMatch.MatchNext(parseResult, start, end, patternsByPriority)) {
					matched = true;
					break;
				}
		} while (matched);

		// expressions can only be validated now, as they may change until everything is parsed (e.g. names are re-resolved as members once they are accessed)
		foreach (Expression expression in parseResult.Expressions) expression.Validate(parseResult);

		// whitespace that is not part of any expression (e.g. before or after the expression) is fine, anything else should have been resolved to one expression
		List<int> elementIndexes = Enumerable.Range(0, parseResult.Count).Where(i => parseResult[i] is not WhitespaceToken).ToList();
		bool hasUnresolvedTokens = false;
		foreach (int i in elementIndexes)
			if (parseResult[i] is not Expression) {
				parseResult.AddExceptionMessage("Unresolved token", parseResult.GetSourceStartIndex(i), parseResult.GetSourceEndIndex(i));
				hasUnresolvedTokens = true;
			}

		// only actions may change anything, everything else is evaluated whenever the UI is rendered
		bool hasStatements = false;
		if (target != ParseTarget.Action && target != ParseTarget.Any)
			foreach (Expression expression in parseResult.Expressions)
				if (expression is AssignmentExpression or SequenceExpression) {
					expression.AddError(parseResult, expression is AssignmentExpression ? "Assignments are only allowed in actions" : "Only actions can have multiple statements (separated by ';')");
					hasStatements = true;
				}

		if (!hasUnresolvedTokens && elementIndexes.Count > 1)
			// if we only have values in the parse result, but there are more than one
			parseResult.AddExceptionMessage("Cannot have more than one expression in a expression expression", parseResult.GetSourceStartIndex(elementIndexes[1]), parseResult.source.Length);
		else if (!hasStatements && elementIndexes.Count == 1 && parseResult[elementIndexes[0]] is Expression root) // statements are not checked any further, as the code is not meant to be one
			ValidateRoot(root, target, resultType, parseResult);

		return parseResult;
	}

	private static void ValidateRoot(Expression root, ParseTarget target, Type resultType, ParseResult parseResult) {
		switch (target) {
			case ParseTarget.Value:
				if (Expression.CheckValue(root, parseResult) && resultType != null && !TypeRules.IsImplicitlyConvertible(root.GetExpectedType(), resultType))
					root.AddError(parseResult, $"The result has to be {TypeNames.GetDisplayName(resultType)}, but is {TypeNames.GetDisplayName(root.GetExpectedType())}");
				break;
			case ParseTarget.Type:
				Expression.CheckType(root, parseResult);
				break;
			case ParseTarget.Action:
				ValidateStatements(root, parseResult);
				break;
		}
	}

	// like in C#, statements have to do something: values that are just computed (like a == b, which might be meant as a = b) would have no effect
	private static void ValidateStatements(Expression statement, ParseResult parseResult) {
		if (statement is SequenceExpression sequence) {
			ValidateStatements(sequence.First, parseResult);
			if (sequence.Second != null) ValidateStatements(sequence.Second, parseResult);
			return;
		}

		if (statement is FunctionCallExpression or AssignmentExpression or ObjectCreationExpression) return;
		if (Expression.DescribeNonValue(statement) is { } problem) statement.AddError(parseResult, problem);
		else if (statement.GetExpectedType() != null) statement.AddError(parseResult, "Only method calls, assignments and object creations can be used as statements");
	}

	/**
	 * Returns the element ranges (start inclusive, end exclusive) patterns should be matched in, in the order they should be tried:
	 * the contents of the innermost parentheses (or square brackets, e.g. of indexers) first, the whole parse result last.
	 * This makes sure everything inside parentheses is resolved before the expressions around them, which could otherwise take parts of an unfinished operand,
	 * e.g. a - b * (c + d) would resolve a - b first, as the multiplication can't match before (c + d) is resolved.
	 */
	private static IEnumerable<(int start, int end)> GetMatchRanges(ParseResult parseResult) {
		List<(int start, int end, int depth)> ranges = new List<(int start, int end, int depth)>();
		Stack<(int index, SingleCharToken closing)> openBrackets = new Stack<(int index, SingleCharToken closing)>();
		for (int i = 0; i < parseResult.Count; i++)
			if (parseResult[i] == SingleCharToken.lparen) {
				openBrackets.Push((i, SingleCharToken.rparen));
			}
			else if (parseResult[i] == SingleCharToken.lbracket) {
				openBrackets.Push((i, SingleCharToken.rbracket));
			}
			else if ((parseResult[i] == SingleCharToken.rparen || parseResult[i] == SingleCharToken.rbracket) && openBrackets.Count > 0 && openBrackets.Peek().closing == parseResult[i]) {
				int start = openBrackets.Pop().index + 1;
				if (start < i) ranges.Add((start, i, openBrackets.Count));
			}

		foreach ((int start, int end, int _) in ranges.OrderByDescending(r => r.depth))
			yield return (start, end);
		yield return (0, parseResult.Count);
	}
}
}
