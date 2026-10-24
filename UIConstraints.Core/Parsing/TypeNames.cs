using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace UIConstraints.Core.Parsing {
/**
 * Converts between types and how they are written in C#.
 */
public static class TypeNames {
	private static readonly Dictionary<Type, string> KEYWORDS = new Dictionary<Type, string> {
		{ typeof(void), "void" }, { typeof(object), "object" }, { typeof(string), "string" }, { typeof(bool), "bool" }, { typeof(char), "char" },
		{ typeof(byte), "byte" }, { typeof(sbyte), "sbyte" }, { typeof(short), "short" }, { typeof(ushort), "ushort" }, { typeof(int), "int" }, { typeof(uint), "uint" },
		{ typeof(long), "long" }, { typeof(ulong), "ulong" }, { typeof(float), "float" }, { typeof(double), "double" }, { typeof(decimal), "decimal" }
	};

	private static readonly Dictionary<string, Type> TYPES_BY_KEYWORD = KEYWORDS.Where(k => k.Key != typeof(void)).ToDictionary(k => k.Value, k => k.Key);

	// keywords that can be used as types (e.g. int for System.Int32)
	public static IEnumerable<string> TypeKeywords => TYPES_BY_KEYWORD.Keys;

	[CanBeNull]
	public static Type GetKeywordType(string keyword) {
		return TYPES_BY_KEYWORD.TryGetValue(keyword, out Type type) ? type : null;
	}

	// the name as usually written in code (assuming the needed usings), e.g. Dictionary<Item, int> instead of Dictionary`2
	public static string GetDisplayName(Type type) {
		if (type == TypeRules.NULL) return "null";
		if (KEYWORDS.TryGetValue(type, out string keyword)) return keyword;
		if (type.IsArray) return GetDisplayName(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
		if (type.IsByRef) return "ref " + GetDisplayName(type.GetElementType());
		if (type.IsGenericParameter) return type.Name;
		if (!type.IsGenericType) return type.Name;
		return GetBaseName(type.Name) + "<" + string.Join(", ", type.GetGenericArguments().Select(GetDisplayName)) + ">";
	}

	// the fully qualified name in C# syntax, which resolves without any usings, e.g. System.Collections.Generic.Dictionary<Example.Item, int>
	public static string GetFullName(Type type) {
		if (KEYWORDS.TryGetValue(type, out string keyword)) return keyword;
		if (type.IsArray) return GetFullName(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
		if (type.IsGenericParameter) return type.Name;

		string prefix = type.IsNested ? GetFullName(type.DeclaringType.IsGenericType ? type.DeclaringType.GetGenericTypeDefinition() : type.DeclaringType) + "." : string.IsNullOrEmpty(type.Namespace) ? "" : type.Namespace + ".";
		string name = GetBaseName(type.Name);
		if (!type.IsGenericType) return prefix + name;
		if (type.IsGenericTypeDefinition) return prefix + name + "<" + new string(',', type.GetGenericArguments().Length - 1) + ">";
		return prefix + name + "<" + string.Join(", ", type.GetGenericArguments().Select(GetFullName)) + ">";
	}

	// the name of a type without the amount of generic parameters, e.g. List for List`1
	public static string GetBaseName(string metadataName) {
		int markerIndex = metadataName.IndexOf('`');
		return markerIndex < 0 ? metadataName : metadataName.Substring(0, markerIndex);
	}

	// the name as written in code for a type that is not known yet, e.g. List<> for List`1 (used in error messages)
	public static string GetDisplayNameOfMetadataName(string metadataName) {
		int markerIndex = metadataName.IndexOf('`');
		if (markerIndex < 0) return metadataName;
		int arity = int.Parse(metadataName.Substring(markerIndex + 1));
		return metadataName.Substring(0, markerIndex) + "<" + new string(',', arity - 1) + ">";
	}
}
}
