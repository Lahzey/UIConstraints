using System.Collections;
using System.Collections.Generic;
using UIConstraints.Core.Parsing.Expressions;

namespace UIConstraints.Core.Parsing {
public class ParseResult : IEnumerable<ParsedElement> {
	private readonly List<ParsedElement> elements = new List<ParsedElement>();

	public readonly Dictionary<int, Expression> expressionsAtPositions = new Dictionary<int, Expression>(); // a mapping used by inspector to determine which expression is at a given position
	public Expression ExpressionAtPosition(int i) => expressionsAtPositions.TryGetValue(i, out Expression value) ? value : null;

	// used for the inspector so it can know what certain source elements map to
	public readonly string source;
	private readonly List<int> sourceIndexes = new List<int>(); // the start indexes of the elements in the source

	public readonly ParseContext context;

	public ParseResult(string source, ParseContext context) {
		this.source = source;
		this.context = context;
		sourceIndexes.Add(0); // the first token always starts at index 0, we always add the index of the next token when adding a token
	}

	public bool Success => Exception == null;

	// the single expression the source was parsed to (whitespace around it is ignored), null if parsing failed or the source is empty
	public Expression Expression {
		get {
			if (!Success) return null;
			foreach (ParsedElement element in elements)
				if (element is not WhitespaceToken)
					return element as Expression;
			return null;
		}
	}

	public ParseException Exception { get; private set; }

	public int Count => elements.Count;

	public ParsedElement this[int index] => elements[index];

	public IEnumerator<ParsedElement> GetEnumerator() {
		return elements.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator() {
		return GetEnumerator();
	}

	public void Add(Token token, int endIndex) // can only add tokens as they are the first step in parsing, values will always replace existing tokens
	{
		elements.Add(token);
		sourceIndexes.Add(endIndex); // the exclusive end index of the element is also the start index of the next element
	}

	public void Replace(int index, ParsedElement newElement) {
		ReplaceRange(index, 1, newElement);
	}

	public void ReplaceRange(int startIndex, int count, ParsedElement newElement) {
		elements.RemoveRange(startIndex, count);
		elements.Insert(startIndex, newElement);

		// the start index of the first element and the next element after the last element are not changed
		sourceIndexes.RemoveRange(startIndex + 1, count - 1);

		// storing the positions of values index by index like this is inefficient, but only has to be done on parse and should save time later
		if (newElement is Expression value)
			for (int i = sourceIndexes[startIndex]; i < sourceIndexes[startIndex + 1]; i++)
				if (value.HidesInspectedChildren() || !expressionsAtPositions.ContainsKey(i))
					expressionsAtPositions[i] = value; // never overwrite existing values, we want the lowest level expression to take priority
	}

	public void AddExceptionMessage(string message, int startPosition, int endPosition) {
		if (Exception == null) Exception = new ParseException();

		Exception.AddMessage(message, startPosition, endPosition);
	}

	public int GetSourceStartIndex(int elementIndex) {
		return sourceIndexes[elementIndex];
	}

	public int GetSourceEndIndex(int elementIndex) {
		return elementIndex == elements.Count - 1 ? source.Length : sourceIndexes[elementIndex + 1];
	}
}
}
