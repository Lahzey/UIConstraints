using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;

namespace UIConstraints.Core.Parsing {
public static class TypeResolver {
	private static readonly Dictionary<string, Type> TYPES_BY_FULL_NAME = new Dictionary<string, Type>(); // also caches failed lookups as null
	private static readonly Dictionary<string, Type> TYPES_BY_CSHARP_NAME = new Dictionary<string, Type>(); // also caches failed lookups as null
	private static readonly object ALL_TYPES_LOCK = new object();
	private static List<Type> allPublicTypes;
	private static HashSet<string> allNamespaces; // including the namespaces containing other namespaces, e.g. System for System.Collections

	// resolves a (non-nested) type name using the global namespace and the given namespaces, returns null if no type or more than one type (ambiguous) was found
	[CanBeNull]
	public static Type Resolve(string metadataName, IEnumerable<string> namespaces) {
		List<Type> candidates = FindCandidates(metadataName, namespaces);
		return candidates.Count == 1 ? candidates[0] : null;
	}

	// all types a (non-nested) name refers to in the global namespace and the given namespaces, more than one means the name is ambiguous
	public static List<Type> FindCandidates(string metadataName, IEnumerable<string> namespaces) {
		List<Type> candidates = new List<Type>();
		Type globalType = FindType(metadataName);
		if (globalType != null) candidates.Add(globalType);
		foreach (string usedNamespace in namespaces) {
			Type type = FindType(usedNamespace + "." + metadataName);
			if (type != null && !candidates.Contains(type)) candidates.Add(type);
		}

		return candidates;
	}

	// finds a public type by its full metadata name in any loaded assembly (e.g. System.Collections.Generic.List`1, nested types separated by '+')
	[CanBeNull]
	public static Type FindType(string fullName) {
		lock (TYPES_BY_FULL_NAME) {
			if (TYPES_BY_FULL_NAME.TryGetValue(fullName, out Type cachedType)) return cachedType;

			Type foundType = null;
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
				Type type = assembly.GetType(fullName, false);
				if (type != null && type.IsVisible) {
					foundType = type;
					break;
				}
			}

			TYPES_BY_FULL_NAME[fullName] = foundType;
			return foundType;
		}
	}

	// resolves a fully qualified type in C# syntax, e.g. System.Collections.Generic.Dictionary<string, Example.Item> (which is how aliases are written), null if it is not a valid type
	[CanBeNull]
	public static Type ResolveFullyQualifiedType(string csharpName) {
		lock (TYPES_BY_CSHARP_NAME) {
			if (TYPES_BY_CSHARP_NAME.TryGetValue(csharpName, out Type cachedType)) return cachedType;
		}

		// parsed outside of the lock, as parsing may resolve other types
		Type type = CodeParser.TryParse(csharpName, ParseContext.EMPTY, ParseTarget.Type).Expression?.GetReferencedType();
		lock (TYPES_BY_CSHARP_NAME) {
			TYPES_BY_CSHARP_NAME[csharpName] = type;
		}

		return type;
	}

	public static bool NamespaceExists(string fullName) {
		return GetAllNamespaces().Contains(fullName);
	}

	// the namespaces directly in the given one (or the root namespaces if null), e.g. Collections for System
	public static IEnumerable<string> GetChildNamespaces([CanBeNull] string parentNamespace) {
		string prefix = parentNamespace == null ? "" : parentNamespace + ".";
		return GetAllNamespaces().Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.Length > prefix.Length && n.IndexOf('.', prefix.Length) < 0).Select(n => n.Substring(prefix.Length));
	}

	/**
	 * Using entries that would make a (non-nested) name resolve to a type: if it is ambiguous with the given namespaces, an alias for each candidate,
	 * otherwise the namespaces of all types with that name. Expensive to call the first time (see GetAllPublicTypes).
	 */
	public static List<string> SuggestUsings(string metadataName, IEnumerable<string> namespaces) {
		List<Type> candidates = FindCandidates(metadataName, namespaces);
		if (candidates.Count > 1) {
			// aliases can only refer to closed types (e.g. List<int>, but not List<>)
			if (candidates.Any(c => c.IsGenericTypeDefinition)) return new List<string>();
			return candidates.Select(t => metadataName + "=" + TypeNames.GetFullName(t)).ToList();
		}

		return GetAllPublicTypes().Where(t => t.Name == metadataName && !string.IsNullOrEmpty(t.Namespace)).Select(t => t.Namespace).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
	}

	/**
	 * All public top level types (including generic type definitions like List`1) of all loaded assemblies, except editor ones (constraints are evaluated at runtime, where those are not available).
	 * Used for suggestions and namespace lookups, expensive to call the first time.
	 */
	public static IReadOnlyList<Type> GetAllPublicTypes() {
		lock (ALL_TYPES_LOCK) {
			if (allPublicTypes != null) return allPublicTypes;

			allPublicTypes = new List<Type>();
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
				if (assembly.IsDynamic || IsEditorAssembly(assembly.GetName().Name)) continue;

				Type[] types;
				try {
					types = assembly.GetExportedTypes();
				}
				catch (Exception) {
					continue; // some assemblies can't be inspected, they are not important enough to be suggested
				}

				foreach (Type type in types)
					if (!type.IsNested && !type.Name.Contains('<') && (type.Namespace == null || !type.Namespace.StartsWith("UnityEditor")))
						allPublicTypes.Add(type);
			}

			return allPublicTypes;
		}
	}

	private static HashSet<string> GetAllNamespaces() {
		IReadOnlyList<Type> types = GetAllPublicTypes();
		lock (ALL_TYPES_LOCK) {
			if (allNamespaces != null) return allNamespaces;

			allNamespaces = new HashSet<string>();
			foreach (Type type in types) {
				string currentNamespace = type.Namespace;
				while (!string.IsNullOrEmpty(currentNamespace) && allNamespaces.Add(currentNamespace)) {
					int lastDot = currentNamespace.LastIndexOf('.');
					currentNamespace = lastDot < 0 ? null : currentNamespace.Substring(0, lastDot);
				}
			}

			return allNamespaces;
		}
	}

	// editor assemblies of Unity and Godot
	private static bool IsEditorAssembly(string assemblyName) {
		return assemblyName.StartsWith("UnityEditor") || assemblyName.EndsWith(".Editor") || assemblyName.Contains(".Editor.") || assemblyName.StartsWith("GodotTools");
	}
}
}
