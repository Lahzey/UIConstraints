using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;

namespace UIConstraints.Core.Parsing.Expressions {
// a resolved method overload and how arguments are passed to it
public class MethodCall {
	public readonly MethodInfo method;
	public readonly bool expandedParams; // whether the arguments for a params array are passed individually (e.g. Mathf.Max(a, b, c))

	public MethodCall(MethodInfo method, bool expandedParams) {
		this.method = method;
		this.expandedParams = expandedParams;
	}
}

/**
 * Reflection helpers to find and use the members that expressions access, following the rules of C# where reasonably possible.
 */
public static class MemberResolver {
	public static BindingFlags GetFlags(bool isStatic) {
		return BindingFlags.Public | (isStatic ? BindingFlags.Static | BindingFlags.FlattenHierarchy : BindingFlags.Instance);
	}

	// all public methods of a type. Interfaces don't list the members of the interfaces they extend or of object (which every value has), so those are added
	public static IEnumerable<MethodInfo> GetMethods(Type type, bool isStatic) {
		BindingFlags flags = GetFlags(isStatic);
		IEnumerable<MethodInfo> methods = type.GetMethods(flags);
		if (type.IsInterface && !isStatic) methods = methods.Concat(type.GetInterfaces().SelectMany(i => i.GetMethods(flags))).Concat(typeof(object).GetMethods(flags));
		return methods;
	}

	// all public properties of a type, including those of extended interfaces
	public static IEnumerable<PropertyInfo> GetProperties(Type type, bool isStatic) {
		BindingFlags flags = GetFlags(isStatic);
		IEnumerable<PropertyInfo> properties = type.GetProperties(flags);
		if (type.IsInterface && !isStatic) properties = properties.Concat(type.GetInterfaces().SelectMany(i => i.GetProperties(flags)));
		return properties;
	}

	// finds a readable field or (non-indexer) property
	[CanBeNull]
	public static MemberInfo FindFieldOrProperty(Type type, string name, bool isStatic) {
		FieldInfo fieldInfo = type.GetField(name, GetFlags(isStatic));
		if (fieldInfo != null) return fieldInfo;

		// not using GetProperty(name), as that throws if there are multiple properties with that name (e.g. a property hiding a property of its base class)
		return GetProperties(type, isStatic).FirstOrDefault(p => p.Name == name && p.GetIndexParameters().Length == 0 && p.GetMethod != null && p.GetMethod.IsPublic);
	}

	[CanBeNull]
	public static Type FindNestedType(Type type, string metadataName) {
		return type.GetNestedType(metadataName, BindingFlags.Public);
	}

	public static bool HasMethod(Type type, string name, bool isStatic) {
		return GetMethods(type, isStatic).Any(m => m.Name == name && !m.ContainsGenericParameters);
	}

	public static object GetValue(MemberInfo member, [CanBeNull] object target, Expression expressionRef) {
		try {
			return member switch {
				FieldInfo fieldInfo => fieldInfo.GetValue(target),
				PropertyInfo propertyInfo => propertyInfo.GetValue(target),
				_ => throw new EvaluationException($"Cannot get the value of '{member.Name}'", expressionRef)
			};
		}
		catch (TargetInvocationException e) {
			throw new EvaluationException($"Getter of '{member.Name}' threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}", expressionRef, e.InnerException);
		}
	}

	/**
	 * Finds the overload of a method that fits the given argument types best, like C# does (TypeRules.NULL can be used for the null literal).
	 * Returns null and sets error if no overload or more than one equally fitting overloads were found. Generic methods are not supported.
	 */
	[CanBeNull]
	public static MethodCall ResolveMethod(Type type, string name, bool isStatic, Type[] argTypes, out string error) {
		error = null;
		List<Candidate> candidates = new List<Candidate>();
		foreach (MethodInfo method in GetMethods(type, isStatic).Distinct()) {
			if (method.Name != name || method.ContainsGenericParameters) continue;
			Candidate candidate = Candidate.Create(method, argTypes, false) ?? Candidate.Create(method, argTypes, true); // the normal form is preferred over the expanded params form
			if (candidate != null) candidates.Add(candidate);
		}

		if (candidates.Count == 0) {
			error = $"No overload of '{name}' takes the arguments ({string.Join(", ", argTypes.Select(TypeNames.GetDisplayName))})";
			return null;
		}

		// the best candidate has to be better than all others
		foreach (Candidate candidate in candidates)
			if (candidates.All(other => other == candidate || candidate.IsBetterThan(other, argTypes)))
				return new MethodCall(candidate.method, candidate.expandedParams);

		List<Candidate> ambiguous = candidates.Where(c => !candidates.Any(other => other != c && other.IsBetterThan(c, argTypes))).ToList();
		if (ambiguous.Count < 2) ambiguous = candidates;
		error = $"The call is ambiguous between {GetSignature(ambiguous[0].method)} and {GetSignature(ambiguous[1].method)}";
		return null;
	}

	public static string GetSignature(MethodInfo method) {
		return $"{method.Name}({string.Join(", ", method.GetParameters().Select(p => TypeNames.GetDisplayName(p.ParameterType)))})";
	}

	private class Candidate {
		public readonly MethodInfo method;
		public readonly bool expandedParams;
		private readonly Type[] parameterTypes; // the parameter type for each argument
		private readonly bool usesDefaults; // whether optional parameters are left out

		private Candidate(MethodInfo method, bool expandedParams, Type[] parameterTypes, bool usesDefaults) {
			this.method = method;
			this.expandedParams = expandedParams;
			this.parameterTypes = parameterTypes;
			this.usesDefaults = usesDefaults;
		}

		// null if the method can't be called with the given argument types in this form
		[CanBeNull]
		public static Candidate Create(MethodInfo method, Type[] argTypes, bool expandedParams) {
			ParameterInfo[] parameters = method.GetParameters();
			if (parameters.Any(p => p.ParameterType.IsByRef)) return null; // ref and out parameters are not supported

			int fixedCount = parameters.Length;
			if (expandedParams) {
				if (parameters.Length == 0 || !parameters[parameters.Length - 1].IsDefined(typeof(ParamArrayAttribute), false)) return null;
				fixedCount--;
				if (argTypes.Length < fixedCount) return null;
			}
			else {
				if (argTypes.Length > parameters.Length) return null;
				for (int i = argTypes.Length; i < parameters.Length; i++)
					if (!parameters[i].IsOptional)
						return null;
			}

			Type[] parameterTypes = new Type[argTypes.Length];
			for (int i = 0; i < argTypes.Length; i++) {
				parameterTypes[i] = i < fixedCount ? parameters[i].ParameterType : parameters[parameters.Length - 1].ParameterType.GetElementType();
				if (!TypeRules.IsImplicitlyConvertible(argTypes[i], parameterTypes[i])) return null;
			}

			return new Candidate(method, expandedParams, parameterTypes, !expandedParams && argTypes.Length < parameters.Length);
		}

		public bool IsBetterThan(Candidate other, Type[] argTypes) {
			bool better = false;
			for (int i = 0; i < argTypes.Length; i++) {
				int comparison = CompareConversions(argTypes[i], parameterTypes[i], other.parameterTypes[i]);
				if (comparison < 0) return false;
				if (comparison > 0) better = true;
			}

			if (better) return true;

			// tie breakers for equally fitting parameters
			if (expandedParams != other.expandedParams) return !expandedParams;
			return usesDefaults != other.usesDefaults && !usesDefaults;
		}

		// which conversion from the argument type fits better (positive for the first, negative for the second, 0 if neither), using C#'s 'better conversion target' rule
		private static int CompareConversions(Type argType, Type first, Type second) {
			if (first == second) return 0;
			if (argType == first) return 1;
			if (argType == second) return -1;
			bool firstToSecond = TypeRules.IsImplicitlyConvertible(first, second);
			bool secondToFirst = TypeRules.IsImplicitlyConvertible(second, first);
			if (firstToSecond && !secondToFirst) return 1;
			if (secondToFirst && !firstToSecond) return -1;
			return 0;
		}
	}

	public static object Invoke(MethodCall call, [CanBeNull] object target, object[] values, Expression expressionRef) {
		MethodInfo method = call.method;
		ParameterInfo[] parameters = method.GetParameters();
		object[] arguments = new object[parameters.Length];
		int fixedCount = call.expandedParams ? parameters.Length - 1 : parameters.Length;
		for (int i = 0; i < fixedCount; i++) {
			Type parameterType = parameters[i].ParameterType;
			if (i < values.Length) {
				arguments[i] = TypeRules.ConvertImplicitly(values[i], parameterType);
			}
			else {
				// optional parameter that was left out
				object defaultValue = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : Type.Missing;
				Type underlying = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
				arguments[i] = underlying.IsEnum && defaultValue != null && defaultValue is not Enum ? Enum.ToObject(underlying, defaultValue) : defaultValue;
			}
		}

		if (call.expandedParams) {
			Type elementType = parameters[parameters.Length - 1].ParameterType.GetElementType();
			Array paramsArray = Array.CreateInstance(elementType, values.Length - fixedCount);
			for (int i = fixedCount; i < values.Length; i++) paramsArray.SetValue(TypeRules.ConvertImplicitly(values[i], elementType), i - fixedCount);
			arguments[parameters.Length - 1] = paramsArray;
		}

		try {
			return method.Invoke(target, arguments);
		}
		catch (TargetInvocationException e) {
			throw new EvaluationException($"Method '{method.Name}' threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}", expressionRef, e.InnerException);
		}
		catch (ArgumentException e) {
			throw new EvaluationException($"Invalid arguments for method '{method.Name}': {e.Message}", expressionRef, e);
		}
	}
}
}
