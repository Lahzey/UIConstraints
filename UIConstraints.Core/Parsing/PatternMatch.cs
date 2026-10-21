using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UIConstraints.Core.Parsing.Expressions;

namespace UIConstraints.Core.Parsing {
// a pattern of an ExpressionPatternAttribute, ready to be matched
public class CompiledPattern {
	public readonly Type expressionType;
	public readonly int priority;
	public readonly Regex regex;

	public CompiledPattern(Type expressionType, string pattern, int priority) {
		this.expressionType = expressionType;
		this.priority = priority;
		regex = new Regex(pattern);
	}
}

public class PatternMatch {
	private readonly string elementsString; // a string representation of the ParseResult elements with spaces between them

	private readonly Match match;
	private readonly ParseResult parseResult;
	private int currentMatchedElementIndex;
	private int currentStringIndex;
	private readonly int firstMatchedElementIndex;

	private readonly List<object> matchedElements; // can contain either ParsedElements or Lists of them

	private PatternMatch(Match match, ParseResult parseResult, string elementsString, int firstElementIndex) {
		this.match = match;
		this.parseResult = parseResult;
		this.elementsString = elementsString;

		currentStringIndex = match.Groups[0].Index;

		// counting the amount of spaces before currentStringIndex will point us to the first ParsedElements within the match
		firstMatchedElementIndex = firstElementIndex;
		for (int i = 0; i < currentStringIndex; i++)
			if (elementsString[i] == ' ')
				firstMatchedElementIndex++;
		currentMatchedElementIndex = firstMatchedElementIndex;

		matchedElements = MatchGroup(0);
	}

	/**
	 * Matches the patterns against the elements from rangeStart (inclusive) to rangeEnd (exclusive) and replaces the first match with its expression.
	 * Patterns are tried in order of priority. If multiple patterns with the same priority match, the leftmost match is used, so that operators of the same priority are resolved from left to right.
	 */
	public static bool MatchNext(ParseResult parseResult, int rangeStart, int rangeEnd, List<List<CompiledPattern>> patternsByPriority) {
		// creates a string list of with a placeholder of each element and the joins them into one string
		string elementsString = string.Join(' ', parseResult.Skip(rangeStart).Take(rangeEnd - rangeStart).Select(m => m.GetPlaceholder()));

		foreach (List<CompiledPattern> patterns in patternsByPriority) {
			Match bestMatch = null;
			CompiledPattern bestPattern = null;
			foreach (CompiledPattern pattern in patterns) {
				Match currentMatch = pattern.regex.Match(elementsString);
				if (currentMatch.Success && (bestMatch == null || currentMatch.Index < bestMatch.Index)) {
					bestMatch = currentMatch;
					bestPattern = pattern;
				}
			}

			if (bestMatch == null) continue;

			// replaces the matched elements with a new instance of the matched type
			new PatternMatch(bestMatch, parseResult, elementsString, rangeStart).ReplaceMatchedElements(bestPattern.expressionType);
			return true;
		}

		return false; // no matches found
	}

	private void ReplaceMatchedElements(Type matchedType) {
		Expression expression = (Expression)Activator.CreateInstance(matchedType);
		expression.Prepare(matchedElements.ToArray(), parseResult.context);
		parseResult.ReplaceRange(firstMatchedElementIndex, currentMatchedElementIndex - firstMatchedElementIndex, expression);
	}

	private List<object> MatchGroup(int groupIndex) {
		List<object> matchedElements = new List<object>();
		Group group = match.Groups[groupIndex];
		Group nextGroup = match.Groups.Count > groupIndex + 1 ? match.Groups[groupIndex + 1] : null;
		int groupEnd = group.Index + group.Length;
		while (currentStringIndex < groupEnd)
			if (nextGroup?.Index == currentStringIndex) {
				// this next group is nested inside this group, let it handle its part of the string and add all its matched elements to this group as one list
				matchedElements.Add(MatchGroup(groupIndex + 1));
			}
			else {
				// step forward in the elementsString until we reach the end of the group or next space, indicating the end of the current element
				while (currentStringIndex < groupEnd) {
					currentStringIndex++;
					if (elementsString[currentStringIndex - 1] == ' ') break; // if we just encountered a space, we are done with this element
				}

				ParsedElement parsedElement = parseResult[currentMatchedElementIndex];
				if (parsedElement is not SingleCharToken && parsedElement is not MultiCharToken && parsedElement is not WhitespaceToken) // no need to add these as they are already contained as is in the pattern and do not carry any additional information
					matchedElements.Add(parsedElement);
				currentMatchedElementIndex++;
			}

		return matchedElements;
	}
}
}
