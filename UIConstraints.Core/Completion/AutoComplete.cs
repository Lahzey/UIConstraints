using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;
using JetBrains.Annotations;

namespace UIConstraints.Core.Completion {
public class AutoCompleteOption {
	public string label; // shown in the list of options
	public string detail; // shown greyed out next to the label, e.g. the type of a field
	public string insertText; // replaces the identifier typed so far
	public int cursorOffset; // where the cursor is placed relative to the end of the inserted text, e.g. -1 to place it inside the parentheses of a method call
	[CanBeNull] public string requiredUsing; // a using entry (namespace or alias) that has to be added for the inserted text to resolve

	internal int matchQuality; // lower is better
	internal int group; // options of lower groups are listed first if they match equally well
}

/**
 * Figures out what to suggest at the cursor. Only the text before the cursor is looked at, so this works while the code is still incomplete:
 * the identifier directly in front of the cursor is the prefix being completed, and if it follows a '.', the expression in front of the '.' is parsed on its own
 * to find out what it is (a value, type or namespace) and suggest its members.
 */
public static class AutoComplete {
	private static readonly int MAX_OPTIONS = 100;
	private static readonly string[] LITERAL_KEYWORDS = { "true", "false", "null" };

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
			Expression receiver = CodeParser.TryParse(beforeCursor.Substring(receiverStart, tokens.GetSourceStartIndex(index) - receiverStart), context, ParseTarget.Any).Expression;
			if (receiver == null) return options;

			string receiverNamespace = receiver.GetReferencedNamespace();
			Type receiverType = receiver.GetReferencedType();
			Type valueType = receiver.GetExpectedType();
			if (receiverNamespace != null) AddNamespaceMemberOptions(options, receiverNamespace, prefix);
			else if (receiverType != null) AddMemberOptions(options, receiverType, true, prefix);
			else if (valueType != null && valueType != typeof(void) && valueType != TypeRules.NULL) AddMemberOptions(options, valueType, false, prefix);
		}
		else {
			// after 'is' and 'as' only types make sense
			bool typesOnly = previous is IdentifierToken { Identifier: "is" or "as" };

			// only suggest at the start of an operand, e.g. not in "player na" or "(a + b) na"
			if (!typesOnly && (previous is IdentifierToken or StringToken or NumberToken || previous == SingleCharToken.rparen || previous == SingleCharToken.rbracket)) return options;
			AddRootOptions(options, prefix, context, explicitRequest, typesOnly);
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
			else if (depth == 0 && token == SingleCharToken.greater) {
				// type arguments, e.g. "Comparer<int>.De", but not a comparison like "a > b.c"
				int genericStart = FindTypeArgumentsStart(tokens, index);
				if (genericStart < 0) break;
				index = genericStart;
			}
			else if (depth == 0 && token is not (IdentifierToken or StringToken or NumberToken or WhitespaceToken) && token != SingleCharToken.dot && token != SingleCharToken.lbracket && token != SingleCharToken.rbracket) {
				break;
			}

			start = index;
		}

		return start < 0 ? -1 : tokens.GetSourceStartIndex(start);
	}

	// the index of the '<' that opens the type arguments closed by the '>' at the given index, -1 if they are not type arguments
	private static int FindTypeArgumentsStart(ParseResult tokens, int closeIndex) {
		int depth = 0;
		for (int i = closeIndex; i >= 0; i--) {
			ParsedElement token = tokens[i];
			if (token == SingleCharToken.greater) {
				depth++;
			}
			else if (token == SingleCharToken.smaller) {
				depth--;
				if (depth == 0) return i;
			}
			else if (token is not (IdentifierToken or WhitespaceToken) && token != SingleCharToken.comma && token != SingleCharToken.dot && token != SingleCharToken.lbracket && token != SingleCharToken.rbracket) {
				return -1;
			}
		}

		return -1;
	}

	private static void AddMemberOptions(List<AutoCompleteOption> options, Type type, bool isStatic, string prefix) {
		BindingFlags flags = MemberResolver.GetFlags(isStatic);

		foreach (FieldInfo fieldInfo in type.GetFields(flags)) {
			if (fieldInfo.IsSpecialName || IsObsolete(fieldInfo)) continue;
			AddOption(options, prefix, fieldInfo.Name, fieldInfo.Name, TypeNames.GetDisplayName(fieldInfo.FieldType), fieldInfo.Name, 0, GetInheritanceDistance(type, fieldInfo.DeclaringType));
		}

		foreach (PropertyInfo propertyInfo in MemberResolver.GetProperties(type, isStatic)) {
			if (propertyInfo.GetIndexParameters().Length > 0 || propertyInfo.GetMethod == null || !propertyInfo.GetMethod.IsPublic || IsObsolete(propertyInfo)) continue;
			AddOption(options, prefix, propertyInfo.Name, propertyInfo.Name, TypeNames.GetDisplayName(propertyInfo.PropertyType), propertyInfo.Name, 0, GetInheritanceDistance(type, propertyInfo.DeclaringType));
		}

		foreach (MethodInfo methodInfo in MemberResolver.GetMethods(type, isStatic).Distinct()) {
			if (methodInfo.IsSpecialName || methodInfo.ContainsGenericParameters || IsObsolete(methodInfo)) continue;
			ParameterInfo[] parameters = methodInfo.GetParameters();
			string label = $"{methodInfo.Name}({string.Join(", ", parameters.Select(p => TypeNames.GetDisplayName(p.ParameterType) + " " + p.Name))})";
			AddOption(options, prefix, methodInfo.Name, label, TypeNames.GetDisplayName(methodInfo.ReturnType), methodInfo.Name + "()", parameters.Length > 0 ? -1 : 0, GetInheritanceDistance(type, methodInfo.DeclaringType));
		}

		if (isStatic)
			foreach (Type nestedType in type.GetNestedTypes(BindingFlags.Public)) {
				if (IsObsolete(nestedType)) continue;
				AddTypeOption(options, prefix, nestedType, GetTypeKind(nestedType), 0);
			}
	}

	// the namespaces and types in a namespace
	private static void AddNamespaceMemberOptions(List<AutoCompleteOption> options, string parentNamespace, string prefix) {
		foreach (string childNamespace in TypeResolver.GetChildNamespaces(parentNamespace))
			AddOption(options, prefix, childNamespace, childNamespace, "namespace", childNamespace, 0, 0);

		foreach (Type type in TypeResolver.GetAllPublicTypes())
			if (type.Namespace == parentNamespace && !IsObsolete(type))
				AddTypeOption(options, prefix, type, GetTypeKind(type), 1);
	}

	private static void AddRootOptions(List<AutoCompleteOption> options, string prefix, ParseContext context, bool explicitRequest, bool typesOnly) {
		if (!typesOnly)
			foreach (KeyValuePair<string, Type> variable in context.variables)
				AddOption(options, prefix, variable.Key, variable.Key, TypeNames.GetDisplayName(variable.Value), variable.Key, 0, 0);

		// without anything typed, there would be way too many types to suggest after every operator
		if (prefix.Length == 0 && !explicitRequest) return;

		foreach (string keyword in typesOnly ? TypeNames.TypeKeywords : LITERAL_KEYWORDS.Concat(TypeNames.TypeKeywords))
			if (keyword.StartsWith(prefix, StringComparison.Ordinal))
				AddOption(options, prefix, keyword, keyword, "keyword", keyword, 0, 3);

		// aliases named like the type they refer to are listed as that type below
		foreach (KeyValuePair<string, string> alias in context.aliases)
			if (!alias.Value.EndsWith("." + alias.Key) && alias.Value != alias.Key)
				AddOption(options, prefix, alias.Key, alias.Key, alias.Value, alias.Key, 0, 1);

		// namespaces, for fully qualified names (only once something is typed, there are many of them)
		if (prefix.Length > 0)
			foreach (string rootNamespace in TypeResolver.GetChildNamespaces(null))
				if (rootNamespace.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
					AddOption(options, prefix, rootNamespace, rootNamespace, "namespace", rootNamespace, 0, 2);

		foreach (Type type in TypeResolver.GetAllPublicTypes()) {
			string name = TypeNames.GetBaseName(type.Name);
			bool inUsedNamespace = string.IsNullOrEmpty(type.Namespace) || context.namespaces.Contains(type.Namespace);
			if (prefix.Length == 0 && !inUsedNamespace) continue; // an explicit request without a prefix only lists types that can already be used
			if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue; // only prefix matches, there are too many types for substring matches to be useful
			if (IsObsolete(type)) continue;

			bool usable = context.ResolveType(type.Name) == type;
			AutoCompleteOption option = AddTypeOption(options, prefix, type, type.Namespace ?? GetTypeKind(type), usable ? 1 : 2);
			if (option == null || usable) continue;

			// adding the namespace would make the name ambiguous if another type with that name can already be used, an alias resolves that (only possible for closed types)
			bool nameTaken = inUsedNamespace || context.aliases.ContainsKey(type.Name) || TypeResolver.FindCandidates(type.Name, context.namespaces).Count > 0;
			option.requiredUsing = nameTaken && !type.IsGenericTypeDefinition ? name + "=" + TypeNames.GetFullName(type) : type.Namespace;
		}
	}

	// generic types are shown with their type parameters (e.g. List<T>), and inserted with the cursor between the brackets
	[CanBeNull]
	private static AutoCompleteOption AddTypeOption(List<AutoCompleteOption> options, string prefix, Type type, string detail, int group) {
		string name = TypeNames.GetBaseName(type.Name);
		if (!type.IsGenericTypeDefinition) return AddOption(options, prefix, name, name, detail, name, 0, group);

		string label = name + "<" + string.Join(", ", type.GetGenericArguments().Select(a => a.Name)) + ">";
		return AddOption(options, prefix, name, label, detail, name + "<>", -1, group);
	}

	[CanBeNull]
	private static AutoCompleteOption AddOption(List<AutoCompleteOption> options, string prefix, string name, string label, string detail, string insertText, int cursorOffset, int group) {
		int matchQuality = GetMatchQuality(name, prefix);
		if (matchQuality < 0) return null;

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
}
}
