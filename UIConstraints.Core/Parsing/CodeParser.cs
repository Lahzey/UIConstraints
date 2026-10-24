using System;
using System.Collections.Generic;
using System.Linq;
using UIConstraints.Core.Parsing.Expressions;

namespace UIConstraints.Core.Parsing {
// what the parsed source has to be
public enum ParseTarget {
	Value, // an expression that can be evaluated
	Type, // a type, e.g. List<int>
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

	public static ParseResult TryParse(string valueString, ParseContext context = null, ParseTarget target = ParseTarget.Value) {
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

		if (!hasUnresolvedTokens && elementIndexes.Count > 1)
			// if we only have values in the parse result, but there are more than one
			parseResult.AddExceptionMessage("Cannot have more than one expression in a expression expression", parseResult.GetSourceStartIndex(elementIndexes[1]), parseResult.source.Length);
		else if (elementIndexes.Count == 1 && parseResult[elementIndexes[0]] is Expression root)
			ValidateRoot(root, target, parseResult);

		return parseResult;
	}

	private static void ValidateRoot(Expression root, ParseTarget target, ParseResult parseResult) {
		switch (target) {
			case ParseTarget.Value:
				Expression.CheckValue(root, parseResult);
				break;
			case ParseTarget.Type:
				Expression.CheckType(root, parseResult);
				break;
		}
	}

	/**
	 * Returns the element ranges (start inclusive, end exclusive) patterns should be matched in, in the order they should be tried:
	 * the contents of the innermost parentheses first, the whole parse result last.
	 * This makes sure everything inside parentheses is resolved before the expressions around them, which could otherwise take parts of an unfinished operand,
	 * e.g. a - b * (c + d) would resolve a - b first, as the multiplication can't match before (c + d) is resolved.
	 */
	private static IEnumerable<(int start, int end)> GetMatchRanges(ParseResult parseResult) {
		List<(int start, int end, int depth)> ranges = new List<(int start, int end, int depth)>();
		Stack<int> openParentheses = new Stack<int>();
		for (int i = 0; i < parseResult.Count; i++)
			if (parseResult[i] == SingleCharToken.lparen) {
				openParentheses.Push(i);
			}
			else if (parseResult[i] == SingleCharToken.rparen && openParentheses.Count > 0) {
				int start = openParentheses.Pop() + 1;
				if (start < i) ranges.Add((start, i, openParentheses.Count));
			}

		foreach ((int start, int end, int _) in ranges.OrderByDescending(r => r.depth))
			yield return (start, end);
		yield return (0, parseResult.Count);
	}
}
}
