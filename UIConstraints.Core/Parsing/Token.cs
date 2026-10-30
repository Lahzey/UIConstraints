using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace UIConstraints.Core.Parsing {
public abstract class Token : ParsedElement {
	public static Token Get(string tokenString) {
		Token[] tokens = tokenString.Length == 1 ? SingleCharToken.VALUES : MultiCharToken.VALUES;
		foreach (Token token in tokens)
			if (token.ToString().Equals(tokenString))
				return token;

		return null;
	}
}

public class IdentifierToken : Token {
	private static readonly string[] KEYWORDS = {
		"if", "else", "while", "for", "return", "break", "continue", "true", "false", "null", "is", "as", "void", "new",
		"object", "string", "bool", "char", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "decimal"
	};

	public IdentifierToken(string identifier) {
		Identifier = identifier;
	}

	public string Identifier { get; }

	public bool IsKeyword() {
		return KEYWORDS.Contains(Identifier);
	}

	// whether a name can be used for a variable: letters, digits and underscores, not starting with a digit and not a keyword
	public static bool IsValidIdentifier(string name) {
		return !string.IsNullOrEmpty(name) && Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$") && !KEYWORDS.Contains(name);
	}

	public override string ToString() {
		return "I";
	}

	public override string GetPlaceholder() {
		// returns the identifier itself if it's a keyword, otherwise (for example for variable names) it returns the base placeholder, which shows the type hierarchy of the token
		return IsKeyword() ? Identifier : base.GetPlaceholder();
	}
}

public class StringToken : Token {
	public StringToken(string value) {
		Value = value;
	}

	public string Value { get; }

	public override string ToString() {
		return "S";
	}
}

public class NumberToken : Token {
	public NumberToken(object value) {
		Value = value;
	}

	public object Value { get; } // can be any type of number (int, float, double, etc.)

	public override string ToString() {
		return "N";
	}

	/**
	 * The value of a number literal with the suffixes of C#: u (uint), l (long), ul (ulong), f (float), d (double) and m (decimal).
	 * Without a suffix, integers are the smallest of int, long and ulong that fits, and unlike in C#, numbers with a decimal point are floats if they fit (as Unity uses floats everywhere).
	 * Returns null if the suffix is invalid or the value does not fit.
	 */
	[CanBeNull]
	private static object ParseNumber(string digits, string suffix) {
		bool floatingPoint = digits.Contains(".");
		NumberStyles style = floatingPoint ? NumberStyles.AllowDecimalPoint : NumberStyles.None;
		CultureInfo culture = CultureInfo.InvariantCulture;
		switch (suffix) {
			case "f":
				return float.TryParse(digits, style, culture, out float f) ? f : null;
			case "d":
				return double.TryParse(digits, style, culture, out double d) ? d : null;
			case "m":
				return decimal.TryParse(digits, style, culture, out decimal m) ? m : null;
			case "" when floatingPoint:
				if (!double.TryParse(digits, style, culture, out double value)) return null;
				return value is <= float.MaxValue and >= float.MinValue ? (object)(float)value : value; // casting to object, otherwise the conditional's type would be double
		}

		if (floatingPoint || !ulong.TryParse(digits, style, culture, out ulong integer)) return null;
		return suffix switch { // the arms are cast to object, otherwise they would all have the type ulong
			"" => integer <= int.MaxValue ? (object)(int)integer : integer <= long.MaxValue ? (long)integer : integer,
			"u" => integer <= uint.MaxValue ? (object)(uint)integer : integer,
			"l" => integer <= long.MaxValue ? (object)(long)integer : null,
			"ul" or "lu" => (object)integer,
			_ => null
		};
	}

	public static void ReplaceTokens(ParseResult parseResult) {
		for (int i = 0; i < parseResult.Count; i++) {
			if (parseResult[i] is not IdentifierToken) continue;

			IdentifierToken identifierToken = (IdentifierToken)parseResult[i];

			Match match = Regex.Match(identifierToken.Identifier, @"^([0-9]+|[0-9]*\.[0-9]+)([a-zA-Z]*)$");
			if (!match.Success) continue;

			object number = ParseNumber(match.Groups[1].Value, match.Groups[2].Value.ToLowerInvariant());
			if (number == null) {
				parseResult.AddExceptionMessage($"'{identifierToken.Identifier}' is not a valid number", parseResult.GetSourceStartIndex(i), parseResult.GetSourceEndIndex(i));
				number = 0; // continues with a valid number, so the rest is still checked, without reporting this again (e.g. as an unknown identifier)
			}

			parseResult.Replace(i, new NumberToken(number));
		}
	}
}

public class WhitespaceToken : Token {
	public static char placeholder = 'W';

	private char whitespace;

	public WhitespaceToken(char whitespace) {
		this.whitespace = whitespace;
	}

	public override string ToString() {
		return placeholder + "";
	}

	public override string GetPlaceholder() {
		return placeholder + "";
	}
}

public class SingleCharToken : Token {
	public static SingleCharToken plus = new SingleCharToken('+');
	public static SingleCharToken minus = new SingleCharToken('-');
	public static SingleCharToken mult = new SingleCharToken('*');
	public static SingleCharToken divide = new SingleCharToken('/');
	public static SingleCharToken modulo = new SingleCharToken('%');
	public static SingleCharToken power = new SingleCharToken('^');
	public static SingleCharToken lparen = new SingleCharToken('(');
	public static SingleCharToken rparen = new SingleCharToken(')');
	public static SingleCharToken lbracket = new SingleCharToken('[');
	public static SingleCharToken rbracket = new SingleCharToken(']');
	public static SingleCharToken assign = new SingleCharToken('=');
	public static SingleCharToken greater = new SingleCharToken('>');
	public static SingleCharToken smaller = new SingleCharToken('<');
	public static SingleCharToken and = new SingleCharToken('&');
	public static SingleCharToken or = new SingleCharToken('|');
	public static SingleCharToken not = new SingleCharToken('!');
	public static SingleCharToken dot = new SingleCharToken('.');
	public static SingleCharToken comma = new SingleCharToken(',');
	public static SingleCharToken cond = new SingleCharToken('?');
	public static SingleCharToken alt = new SingleCharToken(':');
	public static SingleCharToken semicolon = new SingleCharToken(';');

	internal static readonly SingleCharToken[] VALUES = { plus, minus, mult, divide, modulo, power, lparen, rparen, lbracket, rbracket, assign, greater, smaller, and, or, not, dot, comma, cond, alt, semicolon };

	public readonly char c;

	private SingleCharToken(char c) {
		this.c = c;
	}

	public static SingleCharToken Get(char c) {
		foreach (SingleCharToken value in VALUES)
			if (value.c == c)
				return value;
		return null;
	}

	public override string ToString() {
		return "" + c;
	}

	public override string GetPlaceholder() {
		return "" + c;
	}
}

public class MultiCharToken : Token {
	public static MultiCharToken equals = new MultiCharToken(SingleCharToken.assign, SingleCharToken.assign);
	public static MultiCharToken notEquals = new MultiCharToken(SingleCharToken.not, SingleCharToken.assign);
	public static MultiCharToken logicalAnd = new MultiCharToken(SingleCharToken.and, SingleCharToken.and);
	public static MultiCharToken logicalOr = new MultiCharToken(SingleCharToken.or, SingleCharToken.or);
	public static MultiCharToken greaterOrEqual = new MultiCharToken(SingleCharToken.greater, SingleCharToken.assign);
	public static MultiCharToken smallerOrEqual = new MultiCharToken(SingleCharToken.smaller, SingleCharToken.assign);
	public static MultiCharToken addAssign = new MultiCharToken(SingleCharToken.plus, SingleCharToken.assign);
	public static MultiCharToken subtractAssign = new MultiCharToken(SingleCharToken.minus, SingleCharToken.assign);
	public static MultiCharToken multiplyAssign = new MultiCharToken(SingleCharToken.mult, SingleCharToken.assign);
	public static MultiCharToken divideAssign = new MultiCharToken(SingleCharToken.divide, SingleCharToken.assign);

	internal static readonly MultiCharToken[] VALUES = { equals, notEquals, logicalAnd, logicalOr, greaterOrEqual, smallerOrEqual, addAssign, subtractAssign, multiplyAssign, divideAssign };

	private readonly SingleCharToken[] tokens;

	private MultiCharToken(params SingleCharToken[] tokens) {
		this.tokens = tokens;
	}

	public static void ReplaceTokens(ParseResult parseResult) {
		// create a text representation of this token array (one char per token) so we can easily find and replace tokens with MultiCharTokens using string search
		string tokenString = string.Concat(parseResult); // at this point every element is a token

		foreach (MultiCharToken multiCharToken in VALUES) {
			string id = multiCharToken.ToString();
			int index = tokenString.IndexOf(id, StringComparison.Ordinal);
			while (index != -1) {
				parseResult.ReplaceRange(index, id.Length, multiCharToken);
				// keep the text representation in sync with the parse result, otherwise every following index would be off. 'M' is not part of any MultiCharToken, so it can't be matched again
				tokenString = tokenString.Remove(index, id.Length).Insert(index, "M");
				index = tokenString.IndexOf(id, index + 1, StringComparison.Ordinal);
			}
		}
	}

	public override string ToString() {
		string text = "";
		foreach (SingleCharToken token in tokens) text += token.ToString();
		return text;
	}

	public override string GetPlaceholder() {
		return ToString();
	}
}
}
