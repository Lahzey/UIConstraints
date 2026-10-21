using System;
using System.Collections.Generic;
using System.Reflection;
using JetBrains.Annotations;

namespace UIConstraints.Core.Parsing {
public static class TypeResolver {
	private static readonly Dictionary<string, Type> TYPES_BY_FULL_NAME = new Dictionary<string, Type>(); // also caches failed lookups as null
	private static readonly object ALL_TYPES_LOCK = new object();
	private static List<Type> allPublicTypes;

	// resolves a (non-nested) type name using the global namespace and the given usings, returns null if no type or more than one type (ambiguous) was found
	[CanBeNull]
	public static Type Resolve(string name, IEnumerable<string> usings) {
		Type result = FindType(name);
		foreach (string usedNamespace in usings) {
			Type type = FindType(usedNamespace + "." + name);
			if (type == null || type == result) continue;
			if (result != null) return null; // ambiguous, C# would not compile this either
			result = type;
		}

		return result;
	}

	// finds a public type by its full name in any loaded assembly
	[CanBeNull]
	public static Type FindType(string fullName) {
		lock (TYPES_BY_FULL_NAME) {
			if (TYPES_BY_FULL_NAME.TryGetValue(fullName, out Type cachedType)) return cachedType;

			Type foundType = null;
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
				Type type = assembly.GetType(fullName, false);
				if (type != null && type.IsPublic) {
					foundType = type;
					break;
				}
			}

			TYPES_BY_FULL_NAME[fullName] = foundType;
			return foundType;
		}
	}

	/**
	 * All public, non-generic, top level types of all loaded assemblies, except editor ones (constraints are evaluated at runtime, where those are not available).
	 * Used for suggestions in the inspector, expensive to call the first time.
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
					if (!type.IsNested && !type.IsGenericTypeDefinition && (type.Namespace == null || !type.Namespace.StartsWith("UnityEditor")))
						allPublicTypes.Add(type);
			}

			return allPublicTypes;
		}
	}

	// editor assemblies of Unity and Godot
	private static bool IsEditorAssembly(string assemblyName) {
		return assemblyName.StartsWith("UnityEditor") || assemblyName.EndsWith(".Editor") || assemblyName.Contains(".Editor.") || assemblyName.StartsWith("GodotTools");
	}
}
}
