using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;

namespace UIConstraints.Core.Parsing.Expressions {
/**
 * Reflection helpers to find and use the members that expressions access, following the rules of C# where reasonably possible.
 */
public static class MemberResolver {
	// implicit numeric conversions as defined by C#
	private static readonly Dictionary<Type, Type[]> IMPLICIT_NUMERIC_CONVERSIONS = new Dictionary<Type, Type[]> {
		{ typeof(sbyte), new[] { typeof(short), typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal) } },
		{ typeof(byte), new[] { typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) } },
		{ typeof(short), new[] { typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal) } },
		{ typeof(ushort), new[] { typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) } },
		{ typeof(int), new[] { typeof(long), typeof(float), typeof(double), typeof(decimal) } },
		{ typeof(uint), new[] { typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) } },
		{ typeof(long), new[] { typeof(float), typeof(double), typeof(decimal) } },
		{ typeof(ulong), new[] { typeof(float), typeof(double), typeof(decimal) } },
		{ typeof(char), new[] { typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) } },
		{ typeof(float), new[] { typeof(double) } }
	};

	public static BindingFlags GetFlags(bool isStatic) {
		return BindingFlags.Public | (isStatic ? BindingFlags.Static | BindingFlags.FlattenHierarchy : BindingFlags.Instance);
	}

	// finds a readable field or (non-indexer) property
	[CanBeNull]
	public static MemberInfo FindFieldOrProperty(Type type, string name, bool isStatic) {
		BindingFlags flags = GetFlags(isStatic);
		FieldInfo fieldInfo = type.GetField(name, flags);
		if (fieldInfo != null) return fieldInfo;

		// not using GetProperty(name), as that throws if there are multiple properties with that name (e.g. a property hiding a property of its base class)
		return type.GetProperties(flags).FirstOrDefault(p => p.Name == name && p.GetIndexParameters().Length == 0 && p.GetMethod != null && p.GetMethod.IsPublic);
	}

	[CanBeNull]
	public static Type FindNestedType(Type type, string name) {
		return type.GetNestedType(name, BindingFlags.Public);
	}

	public static bool HasMethod(Type type, string name, bool isStatic) {
		return type.GetMethods(GetFlags(isStatic)).Any(m => m.Name == name && !m.ContainsGenericParameters);
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
	 * Finds the overload of a method that fits the given argument types, which may contain null for arguments whose type is not known before evaluation.
	 * Returns null if no or multiple overloads fit (which may be resolvable using the argument values instead).
	 */
	[CanBeNull]
	public static MethodInfo ResolveMethod(Type type, string name, bool isStatic, Type[] argTypes) {
		return ResolveMethod(type, name, isStatic, argTypes, (i, parameterType) => argTypes[i] == null || IsImplicitlyConvertible(argTypes[i], parameterType));
	}

	// finds the overload of a method that fits the given argument values, returns null if no or multiple overloads fit
	[CanBeNull]
	public static MethodInfo ResolveMethodForValues(Type type, string name, bool isStatic, object[] values) {
		Type[] argTypes = values.Select(v => v?.GetType()).ToArray();
		return ResolveMethod(type, name, isStatic, argTypes, (i, parameterType) => values[i] == null ? !parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) != null : IsImplicitlyConvertible(argTypes[i], parameterType));
	}

	private static MethodInfo ResolveMethod(Type type, string name, bool isStatic, Type[] argTypes, Func<int, Type, bool> isArgumentCompatible) {
		List<MethodInfo> candidates = new List<MethodInfo>();
		foreach (MethodInfo method in type.GetMethods(GetFlags(isStatic))) {
			if (method.Name != name || method.ContainsGenericParameters) continue;

			ParameterInfo[] parameters = method.GetParameters();
			if (argTypes.Length > parameters.Length) continue;

			bool compatible = true;
			for (int i = 0; i < parameters.Length && compatible; i++)
				if (i < argTypes.Length) compatible = !parameters[i].ParameterType.IsByRef && isArgumentCompatible(i, parameters[i].ParameterType);
				else compatible = parameters[i].IsOptional; // arguments left out have to be optional
			if (compatible) candidates.Add(method);
		}

		if (candidates.Count <= 1) return candidates.FirstOrDefault();
		if (argTypes.Any(t => t == null)) return null; // can't decide between overloads without knowing all argument types

		// prefer the overloads where the most arguments match exactly (e.g. Max(int, int) over Max(float, float) for two ints), let the default binder decide between those
		int bestExactMatches = candidates.Max(c => CountExactMatches(c, argTypes));
		MethodBase[] bestCandidates = candidates.Where(c => CountExactMatches(c, argTypes) == bestExactMatches).Cast<MethodBase>().ToArray();
		if (bestCandidates.Length == 1) return (MethodInfo)bestCandidates[0];
		try {
			return (MethodInfo)Type.DefaultBinder.SelectMethod(GetFlags(isStatic), bestCandidates, argTypes, null);
		}
		catch (AmbiguousMatchException) {
			return null;
		}
	}

	private static int CountExactMatches(MethodInfo method, Type[] argTypes) {
		ParameterInfo[] parameters = method.GetParameters();
		int count = 0;
		for (int i = 0; i < argTypes.Length; i++)
			if (parameters[i].ParameterType == argTypes[i])
				count++;
		return count - (parameters.Length - argTypes.Length); // unused optional parameters make an overload less fitting
	}

	public static bool IsImplicitlyConvertible(Type from, Type to) {
		if (to.IsAssignableFrom(from)) return true;
		Type nullableUnderlyingType = Nullable.GetUnderlyingType(to);
		if (nullableUnderlyingType != null) return IsImplicitlyConvertible(from, nullableUnderlyingType);
		return IMPLICIT_NUMERIC_CONVERSIONS.TryGetValue(from, out Type[] targets) && Array.IndexOf(targets, to) >= 0;
	}

	public static object Invoke(MethodInfo method, [CanBeNull] object target, object[] values, Expression expressionRef) {
		ParameterInfo[] parameters = method.GetParameters();
		object[] arguments = new object[parameters.Length];
		for (int i = 0; i < parameters.Length; i++) {
			Type parameterType = Nullable.GetUnderlyingType(parameters[i].ParameterType) ?? parameters[i].ParameterType;
			if (i >= values.Length) {
				// optional parameter that was left out
				object defaultValue = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : Type.Missing;
				arguments[i] = parameterType.IsEnum && defaultValue != null && defaultValue is not Enum ? Enum.ToObject(parameterType, defaultValue) : defaultValue;
			}
			else if (values[i] != null && parameterType.IsPrimitive && values[i].GetType() != parameterType && values[i] is IConvertible) {
				arguments[i] = Convert.ChangeType(values[i], parameterType); // implicit numeric conversion, e.g. int to float
			}
			else {
				arguments[i] = values[i];
			}
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
