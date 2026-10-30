using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;

namespace UIConstraints.Core.Parsing.Expressions;

// a resolved method or constructor overload and how arguments are passed to it
public class MethodCall {
    public readonly MethodBase method;
    public readonly bool expandedParams; // whether the arguments for a params array are passed individually (e.g. Mathf.Max(a, b, c))

    public MethodCall(MethodBase method, bool expandedParams) {
        this.method = method;
        this.expandedParams = expandedParams;
    }

    // the type of the value the call results in (the created type for constructors)
    public Type ReturnType => method is MethodInfo methodInfo ? methodInfo.ReturnType : method.DeclaringType;
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

    // genericArity: the number of explicit type arguments (e.g. 1 for GetComponent<Image>()), 0 if none are given, in which case generic methods are found too, as their type arguments may be inferred
    public static bool HasMethod(Type type, string name, bool isStatic, int genericArity = 0) {
        return GetMethods(type, isStatic).Any(m => m.Name == name && (genericArity == 0 || (m.IsGenericMethodDefinition && m.GetGenericArguments().Length == genericArity)));
    }

    public static object GetValue(MemberInfo member, [CanBeNull] object target, Expression expressionRef) {
        if (IsNullableMember(member)) return InvokeNullableMember(member, target, Array.Empty<object>(), expressionRef);
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

    // whether a value can be assigned to a field or property (not readonly, const or without a public setter)
    public static bool IsWritable(MemberInfo member) {
        return member switch {
            FieldInfo fieldInfo => !fieldInfo.IsInitOnly && !fieldInfo.IsLiteral,
            PropertyInfo propertyInfo => propertyInfo.SetMethod != null && propertyInfo.SetMethod.IsPublic,
            _ => false
        };
    }

    public static void SetValue(MemberInfo member, [CanBeNull] object target, object value, Expression expressionRef) {
        try {
            switch (member) {
                case FieldInfo fieldInfo:
                    fieldInfo.SetValue(target, value);
                    break;
                case PropertyInfo propertyInfo:
                    propertyInfo.SetValue(target, value);
                    break;
                default:
                    throw new EvaluationException($"Cannot set the value of '{member.Name}'", expressionRef);
            }
        }
        catch (TargetInvocationException e) {
            throw new EvaluationException($"Setter of '{member.Name}' threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}", expressionRef, e.InnerException);
        }
    }

    // the name of the indexer property of a type (Item for most, Chars for string), its accessors are get_<name> and set_<name>
    public static string GetIndexerName(Type type) {
        return type.GetCustomAttribute<DefaultMemberAttribute>()?.MemberName ?? "Item";
    }

    /**
     * Finds the overload of a method that fits the given argument types best, like C# does (TypeRules.NULL can be used for the null literal).
     * Returns null and sets error if no overload or more than one equally fitting overloads were found. Generic methods are not supported.
     */
    [CanBeNull]
    public static MethodCall ResolveMethod(Type type, string name, bool isStatic, Type[] argTypes, out string error, [CanBeNull] Type[] typeArguments = null) {
        IEnumerable<MethodInfo> methods = GetMethods(type, isStatic).Distinct().Where(m => m.Name == name).Select(m => Construct(m, argTypes, typeArguments)).Where(m => m != null);
        string description = typeArguments == null ? $"'{name}'" : $"'{name}<{string.Join(", ", typeArguments.Select(TypeNames.GetDisplayName))}>'";
        return ResolveOverload(methods, description, argTypes, out error);
    }

    /**
     * The method that can be called with the given arguments: the method itself if it is not generic, otherwise the method constructed with the given type arguments,
     * or the ones inferred from the argument types if there are none. Null if that is not possible (e.g. the type arguments don't fulfill the constraints).
     */
    [CanBeNull]
    private static MethodInfo Construct(MethodInfo method, Type[] argTypes, [CanBeNull] Type[] typeArguments) {
        if (!method.IsGenericMethodDefinition) return typeArguments == null && !method.ContainsGenericParameters ? method : null;

        Type[] genericParameters = method.GetGenericArguments();
        typeArguments ??= InferTypeArguments(method, genericParameters, argTypes);
        if (typeArguments == null || typeArguments.Length != genericParameters.Length) return null;

        try {
            return method.MakeGenericMethod(typeArguments);
        }
        catch (ArgumentException) {
            return null; // the constraints are not fulfilled
        }
    }

    /**
     * Infers the type arguments of a generic method from the types of the arguments, like C# does in the common cases:
     * a parameter T gets the type of its argument, and parameters like IEnumerable<T> or T[] get T from the matching type the argument implements.
     * Returns null if not all type arguments could be inferred.
     */
    [CanBeNull]
    private static Type[] InferTypeArguments(MethodInfo method, Type[] genericParameters, Type[] argTypes) {
        Dictionary<Type, Type> inferred = new Dictionary<Type, Type>();
        ParameterInfo[] parameters = method.GetParameters();
        for (int i = 0; i < argTypes.Length; i++) {
            ParameterInfo parameter = parameters[Math.Min(i, parameters.Length - 1)];
            if (parameters.Length == 0 || (i >= parameters.Length && !parameter.IsDefined(typeof(ParamArrayAttribute), false))) return null;

            Type parameterType = parameter.ParameterType;
            bool expandedParams = parameter.IsDefined(typeof(ParamArrayAttribute), false) && !(argTypes.Length == parameters.Length && argTypes[i].IsArray);
            if (expandedParams) parameterType = parameterType.GetElementType(); // e.g. Max<T>(params T[] values) called as Max(1, 2)
            Infer(parameterType, argTypes[i], inferred);
        }

        if (genericParameters.Any(p => !inferred.ContainsKey(p))) return null;
        return genericParameters.Select(p => inferred[p]).ToArray();
    }

    private static void Infer(Type parameterType, Type argType, Dictionary<Type, Type> inferred) {
        if (argType == TypeRules.NULL || !parameterType.ContainsGenericParameters) return;

        if (parameterType.IsGenericParameter) {
            // the first argument decides, unless a later one is more general (e.g. Max(1, 2.5f) is Max<float>)
            if (!inferred.TryGetValue(parameterType, out Type current) || TypeRules.IsImplicitlyConvertible(current, argType)) inferred[parameterType] = argType;
            return;
        }

        if (parameterType.IsArray) {
            if (argType.IsArray) Infer(parameterType.GetElementType(), argType.GetElementType(), inferred);
            return;
        }

        if (!parameterType.IsGenericType) return;
        Type definition = parameterType.GetGenericTypeDefinition();
        Type match = new[] { argType }.Concat(argType.GetInterfaces()).Concat(GetBaseTypes(argType)).FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == definition);
        if (match == null) return;

        Type[] parameterArguments = parameterType.GetGenericArguments();
        Type[] matchArguments = match.GetGenericArguments();
        for (int i = 0; i < parameterArguments.Length; i++) Infer(parameterArguments[i], matchArguments[i], inferred);
    }

    private static IEnumerable<Type> GetBaseTypes(Type type) {
        for (Type baseType = type.BaseType; baseType != null; baseType = baseType.BaseType) yield return baseType;
    }

    // like ResolveMethod, for the public constructors of a type
    [CanBeNull]
    public static MethodCall ResolveConstructor(Type type, Type[] argTypes, out string error) {
        return ResolveOverload(type.GetConstructors(), $"the constructor of {TypeNames.GetDisplayName(type)}", argTypes, out error);
    }

    [CanBeNull]
    private static MethodCall ResolveOverload(IEnumerable<MethodBase> methods, string description, Type[] argTypes, out string error) {
        error = null;
        List<Candidate> candidates = new List<Candidate>();
        foreach (MethodBase method in methods) {
            Candidate candidate = Candidate.Create(method, argTypes, false) ?? Candidate.Create(method, argTypes, true); // the normal form is preferred over the expanded params form
            if (candidate != null) candidates.Add(candidate);
        }

        if (candidates.Count == 0) {
            error = $"No overload of {description} takes the arguments ({string.Join(", ", argTypes.Select(TypeNames.GetDisplayName))})";
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

    public static string GetSignature(MethodBase method) {
        return $"{(method is ConstructorInfo ? TypeNames.GetDisplayName(method.DeclaringType) : method.Name)}({string.Join(", ", method.GetParameters().Select(p => TypeNames.GetDisplayName(p.ParameterType)))})";
    }

    private class Candidate {
        public readonly MethodBase method;
        public readonly bool expandedParams;
        private readonly Type[] parameterTypes; // the parameter type for each argument
        private readonly bool usesDefaults; // whether optional parameters are left out

        private Candidate(MethodBase method, bool expandedParams, Type[] parameterTypes, bool usesDefaults) {
            this.method = method;
            this.expandedParams = expandedParams;
            this.parameterTypes = parameterTypes;
            this.usesDefaults = usesDefaults;
        }

        // null if the method can't be called with the given argument types in this form
        [CanBeNull]
        public static Candidate Create(MethodBase method, Type[] argTypes, bool expandedParams) {
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
            if (method.IsGenericMethod != other.method.IsGenericMethod) return !method.IsGenericMethod; // e.g. Enumerable.Max(IEnumerable<int>) over Max<int>(IEnumerable<int>)
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

    // whether the member belongs to Nullable<T>, see InvokeNullableMember
    private static bool IsNullableMember(MemberInfo member) {
        return member.DeclaringType is { IsGenericType: true } declaringType && declaringType.GetGenericTypeDefinition() == typeof(Nullable<>);
    }

    /**
     * The members of Nullable<T> (e.g. HasValue or GetValueOrDefault()), done here, as they can't be invoked by reflection:
     * a boxed Nullable<T> is either null or a boxed T, so there is no Nullable<T> to invoke them on.
     */
    private static object InvokeNullableMember(MemberInfo member, [CanBeNull] object target, object[] values, Expression expressionRef) {
        Type underlyingType = Nullable.GetUnderlyingType(member.DeclaringType);
        switch (member.Name) {
            case "HasValue":
                return target != null;
            case "Value":
            case "get_Value":
                return target ?? throw new EvaluationException("The nullable value has no value (it is null)", expressionRef);
            case "GetValueOrDefault":
                return target ?? (values.Length > 0 ? values[0] : Activator.CreateInstance(underlyingType));
            case "ToString":
                return target?.ToString() ?? "";
            case "GetHashCode":
                return target?.GetHashCode() ?? 0;
            case "Equals":
                return Equals(target, values[0]);
            default:
                throw new EvaluationException($"'{member.Name}' of nullable values is not supported", expressionRef);
        }
    }

    // calls the method on the target (null for static methods), or creates an instance for constructors
    public static object Invoke(MethodCall call, [CanBeNull] object target, object[] values, Expression expressionRef) {
        MethodBase method = call.method;
        if (IsNullableMember(method)) return InvokeNullableMember(method, target, values, expressionRef);
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
            return method is ConstructorInfo constructor ? constructor.Invoke(arguments) : method.Invoke(target, arguments);
        }
        catch (TargetInvocationException e) {
            throw new EvaluationException($"Method '{method.Name}' threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}", expressionRef, e.InnerException);
        }
        catch (ArgumentException e) {
            throw new EvaluationException($"Invalid arguments for method '{method.Name}': {e.Message}", expressionRef, e);
        }
    }
}