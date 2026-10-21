using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;
using JetBrains.Annotations;

namespace UIConstraints.Core.Completion {
public class AutoCompleteOption {
	public string label; // shown in the list of options
	public string detail; // shown greyed out next to the label, e.g. the type of a field
	public string insertText; // replaces the identifier typed so far
	public int cursorOffset; // where the cursor is placed relative to the end of the inserted text, e.g. -1 to place it inside the parentheses of a method call
	[CanBeNull] public string requiredNamespace; // a namespace that has to be added to the usings for the inserted text to resolve

	internal int matchQuality; // lower is better
	internal int group; // options of lower groups are listed first if they match equally well
}

/**
 * Figures out what to suggest at the cursor. Only the text before the cursor is looked at, so this works while the code is still incomplete:
 * the identifier directly in front of the cursor is the prefix being completed, and if it follows a '.', the expression in front of the '.' is parsed on its own to find the type whose members are suggested.
 */
public static class AutoComplete {
	private static readonly int MAX_OPTIONS = 100;
	private static readonly string[] LITERAL_KEYWORDS = { "true", "false", "null" };

	private static readonly Dictionary<Type, string> TYPE_ALIASES = new Dictionary<Type, string> {
		{ typeof(void), "void" }, { typeof(object), "object" }, { typeof(string), "string" }, { typeof(bool), "bool" }, { typeof(char), "char" },
		{ typeof(byte), "byte" }, { typeof(sbyte), "sbyte" }, { typeof(short), "short" }, { typeof(ushort), "ushort" }, { typeof(int), "int" }, { typeof(uint), "uint" },
		{ typeof(long), "long" }, { typeof(ulong), "ulong" }, { typeof(float), "float" }, { typeof(double), "double" }, { typeof(decimal), "decimal" }
	};

	/**
	 * Returns the options to suggest for the cursor position, best options first.
	 * replaceStart is set to the start of the identifier being typed, the text from there to the cursor should be replaced by the inserted text of the chosen option.
	 * If explicitRequest is false (suggestions while typing), types are only suggested once at least one character of their name was typed.
	 */
	public static List<AutoCompleteOption> GetOptions(string input, int cursor, ParseContext context, bool explicitRequest, out int replaceStart) {
		replaceStart = cursor;
		List<AutoCompleteOption> options = new List<AutoCompleteOption>();

		string beforeCursor = input.Substring(0, cursor);
		if (beforeCursor.Count(c => c == '"') % 2 == 1) return options; // inside a string

		ParseResult tokens = Tokenizer.Tokenize(beforeCursor, context);
		int index = tokens.Count - 1;

		// the identifier being typed
		string prefix = "";
		if (index >= 0 && tokens[index] is IdentifierToken identifierToken) {
			if (char.IsDigit(identifierToken.Identifier[0])) return options; // a number (or something invalid)
			prefix = identifierToken.Identifier;
			replaceStart = tokens.GetSourceStartIndex(index);
			index--;
		}
		else if (index >= 0 && tokens[index] is NumberToken or StringToken) {
			return options;
		}

		while (index >= 0 && tokens[index] is WhitespaceToken) index--;
		ParsedElement previous = index >= 0 ? tokens[index] : null;

		if (previous == SingleCharToken.dot) {
			// accessing a member of whatever is in front of the dot
			int receiverStart = FindReceiverStart(tokens, index - 1);
			if (receiverStart < 0) return options;
			Expression receiver = CodeParser.TryParse(beforeCursor.Substring(receiverStart, tokens.GetSourceStartIndex(index) - receiverStart), context).Expression;
			if (receiver == null) return options;

			Type staticType = receiver.GetReferencedType();
			Type type = staticType ?? receiver.GetExpectedType();
			if (type == null || type == typeof(object)) return options; // not known before evaluation

			AddMemberOptions(options, type, staticType != null, prefix);
		}
		else {
			// only suggest at the start of an operand, e.g. not in "player na" or "(a + b) na"
			if (previous is IdentifierToken or StringToken or NumberToken || previous == SingleCharToken.rparen) return options;
			AddRootOptions(options, prefix, context, explicitRequest);
		}

		options.Sort((a, b) => {
			int comparison = a.matchQuality.CompareTo(b.matchQuality);
			if (comparison == 0) comparison = a.group.CompareTo(b.group);
			if (comparison == 0) comparison = string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase);
			return comparison;
		});
		if (options.Count > MAX_OPTIONS) options.RemoveRange(MAX_OPTIONS, options.Count - MAX_OPTIONS);
		return options;
	}

	// walks back from the given token index over everything that can be part of the expression in front of a '.', e.g. "player.GetItem(0)" in "x + player.GetItem(0).na"
	private static int FindReceiverStart(ParseResult tokens, int index) {
		int depth = 0;
		int start = -1;
		for (; index >= 0; index--) {
			ParsedElement token = tokens[index];
			if (token == SingleCharToken.rparen) {
				depth++;
			}
			else if (token == SingleCharToken.lparen) {
				if (depth == 0) break; // an unclosed parenthesis is not part of the receiver, e.g. in "Mathf.Max(player.na"
				depth--;
			}
			else if (depth == 0 && token is not (IdentifierToken or StringToken or NumberToken or WhitespaceToken) && token != SingleCharToken.dot) {
				break;
			}

			start = index;
		}

		return start < 0 ? -1 : tokens.GetSourceStartIndex(start);
	}

	private static void AddMemberOptions(List<AutoCompleteOption> options, Type type, bool isStatic, string prefix) {
		BindingFlags flags = MemberResolver.GetFlags(isStatic);

		foreach (FieldInfo fieldInfo in type.GetFields(flags)) {
			if (fieldInfo.IsSpecialName || IsObsolete(fieldInfo)) continue;
			AddOption(options, prefix, fieldInfo.Name, fieldInfo.Name, GetTypeName(fieldInfo.FieldType), fieldInfo.Name, 0, GetInheritanceDistance(type, fieldInfo.DeclaringType));
		}

		foreach (PropertyInfo propertyInfo in type.GetProperties(flags)) {
			if (propertyInfo.GetIndexParameters().Length > 0 || propertyInfo.GetMethod == null || !propertyInfo.GetMethod.IsPublic || IsObsolete(propertyInfo)) continue;
			AddOption(options, prefix, propertyInfo.Name, propertyInfo.Name, GetTypeName(propertyInfo.PropertyType), propertyInfo.Name, 0, GetInheritanceDistance(type, propertyInfo.DeclaringType));
		}

		foreach (MethodInfo methodInfo in type.GetMethods(flags)) {
			if (methodInfo.IsSpecialName || methodInfo.ContainsGenericParameters || IsObsolete(methodInfo)) continue;
			ParameterInfo[] parameters = methodInfo.GetParameters();
			string label = $"{methodInfo.Name}({string.Join(", ", parameters.Select(p => GetTypeName(p.ParameterType) + " " + p.Name))})";
			AddOption(options, prefix, methodInfo.Name, label, GetTypeName(methodInfo.ReturnType), methodInfo.Name + "()", parameters.Length > 0 ? -1 : 0, GetInheritanceDistance(type, methodInfo.DeclaringType));
		}

		if (isStatic)
			foreach (Type nestedType in type.GetNestedTypes(BindingFlags.Public)) {
				if (nestedType.IsGenericTypeDefinition || IsObsolete(nestedType)) continue;
				AddOption(options, prefix, nestedType.Name, nestedType.Name, GetTypeKind(nestedType), nestedType.Name, 0, 0);
			}
	}

	private static void AddRootOptions(List<AutoCompleteOption> options, string prefix, ParseContext context, bool explicitRequest) {
		foreach (DataKeyBase dataKey in DataKeyBase.GetAll())
			AddOption(options, prefix, dataKey.key, dataKey.key, GetTypeName(dataKey.type), dataKey.key, 0, 0);

		// without anything typed, there would be way too many types to suggest after every operator
		if (prefix.Length == 0 && !explicitRequest) return;

		foreach (string keyword in LITERAL_KEYWORDS)
			if (keyword.StartsWith(prefix, StringComparison.Ordinal))
				AddOption(options, prefix, keyword, keyword, "keyword", keyword, 0, 3);

		foreach (Type type in TypeResolver.GetAllPublicTypes()) {
			bool imported = string.IsNullOrEmpty(type.Namespace) || context.usings.Contains(type.Namespace);
			if (prefix.Length == 0 && !imported) continue; // an explicit request without a prefix only lists types that can already be used
			if (!type.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue; // only prefix matches, there are too many types for substring matches to be useful
			if (IsObsolete(type)) continue;

			AutoCompleteOption option = AddOption(options, prefix, type.Name, type.Name, type.Namespace ?? GetTypeKind(type), type.Name, 0, imported ? 1 : 2);
			if (option != null && !imported) option.requiredNamespace = type.Namespace;
		}
	}

	[CanBeNull]
	private static AutoCompleteOption AddOption(List<AutoCompleteOption> options, string prefix, string name, string label, string detail, string insertText, int cursorOffset, int group) {
		int matchQuality = GetMatchQuality(name, prefix);
		if (matchQuality < 0) return null;
		if (insertText == prefix) return null; // already typed out completely, nothing to complete

		AutoCompleteOption option = new AutoCompleteOption {
			label = label,
			detail = detail,
			insertText = insertText,
			cursorOffset = cursorOffset,
			matchQuality = matchQuality,
			group = group
		};
		options.Add(option);
		return option;
	}

	// returns how well the name matches the typed prefix (lower is better), or -1 if it doesn't match at all
	private static int GetMatchQuality(string name, string prefix) {
		if (prefix.Length == 0 || name.StartsWith(prefix, StringComparison.Ordinal)) return 0;
		if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return 1;
		if (name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0) return 2;
		return -1;
	}

	// members declared by the type itself are more interesting than inherited ones (e.g. those of MonoBehaviour or object)
	private static int GetInheritanceDistance(Type type, Type declaringType) {
		int distance = 0;
		while (type != null && type != declaringType) {
			type = type.BaseType;
			distance++;
		}

		return distance;
	}

	private static bool IsObsolete(MemberInfo memberInfo) {
		return memberInfo.IsDefined(typeof(ObsoleteAttribute), true);
	}

	private static string GetTypeKind(Type type) {
		if (type.IsEnum) return "enum";
		if (type.IsInterface) return "interface";
		return type.IsValueType ? "struct" : "class";
	}

	// the name of a type as it would be written in C#, e.g. Dictionary<Item, int> instead of Dictionary`2
	public static string GetTypeName(Type type) {
		if (TYPE_ALIASES.TryGetValue(type, out string alias)) return alias;
		if (type.IsArray) return GetTypeName(type.GetElementType()) + "[]";
		if (type.IsByRef) return "ref " + GetTypeName(type.GetElementType());
		if (!type.IsGenericType) return type.Name;

		string name = type.Name;
		int genericMarkerIndex = name.IndexOf('`');
		if (genericMarkerIndex >= 0) name = name.Substring(0, genericMarkerIndex);
		return name + "<" + string.Join(", ", type.GetGenericArguments().Select(GetTypeName)) + ">";
	}
}
}
