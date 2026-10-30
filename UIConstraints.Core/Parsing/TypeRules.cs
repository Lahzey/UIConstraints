using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using UIConstraints.Core.Parsing.Expressions;

namespace UIConstraints.Core.Parsing;

/**
 * Which types can be converted to which, and the conversions themselves, following C# where reasonably possible.
 */
public static class TypeRules {
    // the type of the null literal, which converts to any reference type or nullable value type
    public static readonly Type NULL = typeof(NullLiteral);

    private sealed class NullLiteral { }

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

    private static readonly HashSet<Type> NUMERIC_TYPES = new HashSet<Type> {
        typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(char), typeof(float), typeof(double), typeof(decimal)
    };

    // numbers and char, which C# treats as a numeric type
    public static bool IsNumeric(Type type) {
        return NUMERIC_TYPES.Contains(type);
    }

    public static bool IsIntegral(Type type) {
        return IsNumeric(type) && type != typeof(float) && type != typeof(double) && type != typeof(decimal);
    }

    public static bool CanBeNull(Type type) {
        return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
    }

    public static bool IsImplicitlyConvertible(Type from, Type to) {
        if (from == to) return true;
        if (from == NULL) return CanBeNull(to);
        if (to.IsAssignableFrom(from)) return true; // also covers boxing and interfaces

        Type nullableUnderlyingType = Nullable.GetUnderlyingType(to);
        if (nullableUnderlyingType != null) return IsImplicitlyConvertible(from, nullableUnderlyingType);
        if (IMPLICIT_NUMERIC_CONVERSIONS.TryGetValue(from, out Type[] targets) && Array.IndexOf(targets, to) >= 0) return true;
        return FindUserDefinedConversion(from, to, false) != null;
    }

    // whether a cast (T)x is allowed
    public static bool IsExplicitlyConvertible(Type from, Type to) {
        if (IsImplicitlyConvertible(from, to)) return true;
        if (from == NULL) return false;

        Type fromUnderlying = Nullable.GetUnderlyingType(from) ?? from;
        Type toUnderlying = Nullable.GetUnderlyingType(to) ?? to;
        if (IsNumericOrEnum(fromUnderlying) && IsNumericOrEnum(toUnderlying)) return true;
        if (IsReferenceConvertible(from, to)) return true;
        return FindUserDefinedConversion(from, to, true) != null;
    }

    // whether converting may work by just checking the type of the value (down casts, unboxing, interfaces), which is what 'as' does
    public static bool IsReferenceConvertible(Type from, Type to) {
        if (from == NULL) return CanBeNull(to);
        Type toUnderlying = Nullable.GetUnderlyingType(to) ?? to;
        if (to.IsAssignableFrom(from) || from.IsAssignableFrom(to) || from.IsAssignableFrom(toUnderlying)) return true;
        return (to.IsInterface && !from.IsSealed) || (from.IsInterface && !to.IsSealed);
    }

    // whether == and != can be used on values of these types
    public static bool IsEqualityComparable(Type a, Type b) {
        if (a == NULL || b == NULL) return CanBeNull(a == NULL ? b : a) || (a == NULL && b == NULL);
        if (IsNumeric(a) && IsNumeric(b)) return true;
        if (a.IsEnum || b.IsEnum) return a == b;
        return IsReferenceConvertible(a, b) || IsImplicitlyConvertible(a, b) || IsImplicitlyConvertible(b, a);
    }

    // the type of a conditional expression with branches of the given types (like in C#, one has to be convertible to the other), null if there is none
    [CanBeNull]
    public static Type GetCommonType(Type a, Type b) {
        if (a == b) return a;
        bool aToB = IsImplicitlyConvertible(a, b);
        bool bToA = IsImplicitlyConvertible(b, a);
        if (aToB && !bToA) return b;
        if (bToA && !aToB) return a;
        return null;
    }

    // the type of the elements a foreach loop over a value of the given type would get, e.g. KeyValuePair<Item, int> for Dictionary<Item, int> (object for non-generic collections)
    public static Type GetElementType(Type enumerableType) {
        if (enumerableType.IsArray) return enumerableType.GetElementType();
        if (enumerableType == typeof(string)) return typeof(char);

        IEnumerable<Type> candidates = enumerableType.IsInterface ? enumerableType.GetInterfaces().Prepend(enumerableType) : enumerableType.GetInterfaces();
        foreach (Type candidate in candidates)
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return candidate.GetGenericArguments()[0];
        return typeof(object);
    }

    private static bool IsNumericOrEnum(Type type) {
        return IsNumeric(type) || type.IsEnum;
    }

    // an op_Implicit (or op_Explicit if allowed) operator converting from one type to the other
    [CanBeNull]
    public static MethodInfo FindUserDefinedConversion(Type from, Type to, bool includeExplicit) {
        if (from == NULL) return null;
        foreach (Type declaringType in new[] { from, to })
        foreach (MethodInfo method in declaringType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)) {
            if (method.Name != "op_Implicit" && !(includeExplicit && method.Name == "op_Explicit")) continue;
            if (method.ReturnType != to) continue;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(from)) return method;
        }

        return null;
    }

    // converts a value to a type it is implicitly convertible to (e.g. an int to a float, or a Vector3 to a Vector2)
    public static object ConvertImplicitly(object value, Type to) {
        if (value == null || to.IsInstanceOfType(value)) return value;
        Type underlying = Nullable.GetUnderlyingType(to) ?? to;
        if (underlying.IsInstanceOfType(value)) return value; // boxed nullables are boxed values of their underlying type
        if (IsNumeric(value.GetType()) && IsNumeric(underlying)) return ConvertNumeric(value, underlying);

        MethodInfo conversion = FindUserDefinedConversion(value.GetType(), to, false) ?? FindUserDefinedConversion(value.GetType(), underlying, false);
        return conversion != null ? conversion.Invoke(null, new[] { value }) : value;
    }

    // performs a cast (T)x
    public static object Cast(object value, Type to, Expression expressionRef) {
        Type underlying = Nullable.GetUnderlyingType(to) ?? to;
        if (value == null) {
            if (!CanBeNull(to)) throw new EvaluationException($"Cannot cast null to {TypeNames.GetDisplayName(to)}", expressionRef);
            return null;
        }

        if (underlying.IsInstanceOfType(value)) return value;
        Type from = value.GetType();
        if (IsNumericOrEnum(from) && IsNumericOrEnum(underlying)) return ConvertNumeric(value, underlying);

        MethodInfo conversion = FindUserDefinedConversion(from, to, true) ?? FindUserDefinedConversion(from, underlying, true);
        if (conversion != null) {
            try {
                return conversion.Invoke(null, new[] { value });
            }
            catch (TargetInvocationException e) {
                throw new EvaluationException($"Conversion from {TypeNames.GetDisplayName(from)} to {TypeNames.GetDisplayName(to)} threw {e.InnerException?.GetType().Name}: {e.InnerException?.Message}", expressionRef, e.InnerException);
            }
        }

        throw new EvaluationException($"Cannot cast {TypeNames.GetDisplayName(from)} to {TypeNames.GetDisplayName(to)}", expressionRef);
    }

    // converts between numeric types (including char and enums) the way C# casts do, e.g. truncating decimals when casting to an integer type
    private static object ConvertNumeric(object value, Type to) {
        Type numericTarget = to.IsEnum ? Enum.GetUnderlyingType(to) : to;
        object result;
        if (value is float or double or decimal) {
            if (numericTarget == typeof(decimal)) result = Convert.ToDecimal(value);
            else result = FromDouble(Convert.ToDouble(value), numericTarget);
        }
        else {
            long integer = value is ulong unsignedLong ? unchecked((long)unsignedLong) : Convert.ToInt64(value); // also handles char and enums
            result = numericTarget == typeof(decimal) ? (decimal)integer : FromLong(integer, numericTarget);
        }

        return to.IsEnum ? Enum.ToObject(to, result) : result;
    }

    private static object FromDouble(double value, Type to) {
        unchecked {
            if (to == typeof(double)) return value;
            if (to == typeof(float)) return (float)value;
            if (to == typeof(long)) return (long)value;
            if (to == typeof(ulong)) return (ulong)value;
            if (to == typeof(int)) return (int)value;
            if (to == typeof(uint)) return (uint)value;
            if (to == typeof(short)) return (short)value;
            if (to == typeof(ushort)) return (ushort)value;
            if (to == typeof(sbyte)) return (sbyte)value;
            if (to == typeof(byte)) return (byte)value;
            if (to == typeof(char)) return (char)value;
        }

        throw new NotImplementedException($"Unimplemented numeric conversion to {to.FullName}");
    }

    private static object FromLong(long value, Type to) {
        unchecked {
            if (to == typeof(long)) return value;
            if (to == typeof(ulong)) return (ulong)value;
            if (to == typeof(int)) return (int)value;
            if (to == typeof(uint)) return (uint)value;
            if (to == typeof(short)) return (short)value;
            if (to == typeof(ushort)) return (ushort)value;
            if (to == typeof(sbyte)) return (sbyte)value;
            if (to == typeof(byte)) return (byte)value;
            if (to == typeof(char)) return (char)value;
            if (to == typeof(double)) return (double)value;
            if (to == typeof(float)) return (float)value;
        }

        throw new NotImplementedException($"Unimplemented numeric conversion to {to.FullName}");
    }
}