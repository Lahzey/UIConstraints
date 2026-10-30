using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace UIConstraints.Core.Parsing {
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)] // an expression can have multiple forms (e.g. with and without a trailing part)
public class ExpressionPatternAttribute : Attribute {
	private readonly object[] patternParts;

	public ExpressionPatternAttribute(params object[] patternParts) {
		this.patternParts = patternParts;
	}

	public bool AutoSpace { get; set; } = true;

	public string GetPattern() {
		List<string> pattern = new List<string>();
		foreach (object patternPart in patternParts)
			if (patternPart is Type type) pattern.Add(TypeOfPattern(type));
			else if (patternPart is string text) pattern.Add(text);
			else throw new ArgumentException("ExpressionPatternAttribute only accepts strings and types as arguments.");

		string joined = string.Join(AutoSpace ? " " : "", pattern);
		StringBuilder resultBuilder = new StringBuilder();
		foreach (char c in joined)
			if (c == ' ') resultBuilder.Append(@" (?:" + WhitespaceToken.placeholder + @" )*");
			else resultBuilder.Append(c);
		return resultBuilder.ToString();
	}

	public static string TypeOfPattern(Type type) {
		// return a regex pattern that matches the type plus any additional type names after a trailing '>' that would denote a subtype (of any depth).
		// The placeholder has to end there (followed by a space or the end), otherwise a pattern could match only a part of a placeholder (e.g. of a subtype), which would break lookaheads after it
		return Regex.Escape(TypeOfPlaceholder(type)) + @"(?:>[a-zA-Z0-9_\.+]+)*(?![^\x20])"; // \x20 instead of a space, as spaces in patterns are replaced (see GetPattern)
	}

	public static string TypeOfPlaceholder(Type type) {
		string typeHierarchy = type.FullName;
		while (type.BaseType != null) {
			type = type.BaseType;
			typeHierarchy = type.FullName + ">" + typeHierarchy;
		}

		return "$" + typeHierarchy;
	}
}

[AttributeUsage(AttributeTargets.Class)]
public class PatternPriorityAttribute : Attribute {
	public readonly int priority;

	public PatternPriorityAttribute(int priority) {
		this.priority = priority;
	}
}
}