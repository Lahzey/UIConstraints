using System;
using System.Collections.Generic;
using System.Reflection;
using JetBrains.Annotations;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;

namespace UIConstraints.Core.Animation;

// how interpolated values of integer types are rounded
public enum IntegerRounding {
    Nearest,
    TowardsStart, // the value only changes once the next whole number is reached (e.g. a counter going from 10 to 20 shows 11 only once 11 is reached)
    TowardsTarget
}

/**
 * The arithmetic transitions need for the values they animate: numbers natively, other types through their operators (+, - and * by a number),
 * which Vector2, Vector3 and Color of both Unity and Godot have. Types without those (or with better ways to blend them) can be registered by the engines.
 */
public static class ValueBlending {
    private sealed class Arithmetic {
        public Func<object, object, object> add;
        public Func<object, object, object> subtract;
        public Func<object, double, object> scale;
        [CanBeNull] public Func<object, double> magnitude; // the size of a difference, for transitions that depend on how far a value moves
        [CanBeNull] public Func<object, object, double, object> lerp; // replaces from + (to - from) * t
    }

    private static readonly Dictionary<Type, Arithmetic> ARITHMETIC = new Dictionary<Type, Arithmetic>(); // null for types that can't be blended

    /**
     * Makes a type blendable, or changes how it is blended (e.g. to blend colors in another color space).
     * magnitude: the size of a difference (e.g. the length of a vector), lerp: blends two values directly, instead of from + (to - from) * t.
     */
    public static void Register<T>(Func<T, T, T> add, Func<T, T, T> subtract, Func<T, double, T> scale, [CanBeNull] Func<T, double> magnitude = null, [CanBeNull] Func<T, T, double, T> lerp = null) {
        lock (ARITHMETIC) {
            ARITHMETIC[typeof(T)] = new Arithmetic {
                add = (a, b) => add((T)a, (T)b),
                subtract = (a, b) => subtract((T)a, (T)b),
                scale = (a, factor) => scale((T)a, factor),
                magnitude = magnitude == null ? null : a => magnitude((T)a),
                lerp = lerp == null ? null : (a, b, t) => lerp((T)a, (T)b, t)
            };
        }
    }

    // numbers (including char and decimal), and types that are registered or have the operators + and - with themselves and * with a number
    public static bool CanBlend(Type type) {
        return TypeRules.IsNumeric(type) || GetArithmetic(type) != null;
    }

    // from + (to - from) * t, t may be outside of 0 to 1 (e.g. for curves that overshoot). Integers are rounded
    public static object Lerp(object from, object to, double t, Type type, IntegerRounding rounding = IntegerRounding.Nearest) {
        if (TypeRules.IsNumeric(type)) {
            double start = ToDouble(from);
            double end = ToDouble(to);
            return FromDouble(start + (end - start) * t, type, rounding, start, end);
        }

        Arithmetic arithmetic = RequireArithmetic(type);
        if (arithmetic.lerp != null) return arithmetic.lerp(from, to, t);
        return arithmetic.add(from, arithmetic.scale(arithmetic.subtract(to, from), t));
    }

    // the arithmetic itself, for transitions that need more than Lerp (e.g. springs). Numbers are doubles here, see ToWorkingValue
    public static object Add(object a, object b) => a is double x ? x + (double)b : RequireArithmetic(a.GetType()).add(a, b);
    public static object Subtract(object a, object b) => a is double x ? x - (double)b : RequireArithmetic(a.GetType()).subtract(a, b);
    public static object Scale(object a, double factor) => a is double x ? x * factor : RequireArithmetic(a.GetType()).scale(a, factor);

    // numbers are calculated with as doubles, other types as they are
    public static object ToWorkingValue(object value, Type type) => TypeRules.IsNumeric(type) ? ToDouble(value) : value;

    public static object FromWorkingValue(object value, Type type, IntegerRounding rounding = IntegerRounding.Nearest) {
        return TypeRules.IsNumeric(type) ? FromDouble((double)value, type, rounding, (double)value, (double)value) : value;
    }

    // the size of a working value (see ToWorkingValue), e.g. a difference. NaN if the type has no magnitude
    public static double Magnitude(object value) {
        if (value is double number) return Math.Abs(number);
        return RequireArithmetic(value.GetType()).magnitude?.Invoke(value) ?? double.NaN;
    }

    // how far apart two values are, NaN if that can't be measured (e.g. for colors)
    public static double Distance(object from, object to, Type type) {
        if (from == null || to == null) return double.NaN;
        if (TypeRules.IsNumeric(type)) return Math.Abs(ToDouble(to) - ToDouble(from));
        Arithmetic arithmetic = GetArithmetic(type);
        return arithmetic?.magnitude?.Invoke(arithmetic.subtract(to, from)) ?? double.NaN;
    }

    // 1 if the value increases, -1 if it decreases, 0 if it can't be compared (numbers, bools and enums can)
    public static int Compare(object from, object to) {
        if (from == null || to == null) return 0;
        if (from is bool fromBool && to is bool toBool) return fromBool.CompareTo(toBool);
        if ((TypeRules.IsNumeric(from.GetType()) || from is Enum) && (TypeRules.IsNumeric(to.GetType()) || to is Enum)) return Math.Sign(ToDouble(to).CompareTo(ToDouble(from)));
        return 0;
    }

    public static double ToDouble(object value) => Convert.ToDouble(value);

    private static object FromDouble(double value, Type type, IntegerRounding rounding, double start, double end) {
        if (type == typeof(double)) return value;
        if (type == typeof(float)) return (float)value;
        if (type == typeof(decimal)) return (decimal)value;

        bool increasing = end >= start;
        double rounded = rounding switch {
            IntegerRounding.TowardsStart => increasing ? Math.Floor(value) : Math.Ceiling(value),
            IntegerRounding.TowardsTarget => increasing ? Math.Ceiling(value) : Math.Floor(value),
            _ => Math.Round(value, MidpointRounding.AwayFromZero)
        };
        return Convert.ChangeType(rounded, type);
    }

    private static Arithmetic RequireArithmetic(Type type) {
        return GetArithmetic(type) ?? throw new InvalidOperationException($"{TypeNames.GetDisplayName(type)} can't be blended");
    }

    [CanBeNull]
    private static Arithmetic GetArithmetic(Type type) {
        lock (ARITHMETIC) {
            if (!ARITHMETIC.TryGetValue(type, out Arithmetic arithmetic)) ARITHMETIC[type] = arithmetic = FindOperators(type);
            return arithmetic;
        }
    }

    // the arithmetic of a type through its operators (op_Addition etc.), null if it does not have all of them
    [CanBeNull]
    private static Arithmetic FindOperators(Type type) {
        if (type.IsPrimitive || type == typeof(string) || type.IsEnum) return null;

        MethodCall add = MemberResolver.ResolveMethod(type, "op_Addition", true, new[] { type, type }, out _);
        MethodCall subtract = MemberResolver.ResolveMethod(type, "op_Subtraction", true, new[] { type, type }, out _);
        MethodCall scale = MemberResolver.ResolveMethod(type, "op_Multiply", true, new[] { type, typeof(float) }, out _);
        if (add == null || subtract == null || scale == null || add.ReturnType != type || subtract.ReturnType != type || scale.ReturnType != type) return null;

        Type scaleType = ((MethodInfo)scale.method).GetParameters()[1].ParameterType; // float in Unity, may be double elsewhere
        Func<object, double> magnitude = FindMagnitude(type);
        return new Arithmetic {
            add = (a, b) => ((MethodInfo)add.method).Invoke(null, new[] { a, b }),
            subtract = (a, b) => ((MethodInfo)subtract.method).Invoke(null, new[] { a, b }),
            scale = (a, factor) => ((MethodInfo)scale.method).Invoke(null, new[] { a, Convert.ChangeType(factor, scaleType) }),
            magnitude = magnitude
        };
    }

    // the length of vectors: magnitude in Unity, Length() in Godot
    [CanBeNull]
    private static Func<object, double> FindMagnitude(Type type) {
        PropertyInfo property = type.GetProperty("magnitude", BindingFlags.Public | BindingFlags.Instance);
        if (property != null && TypeRules.IsNumeric(property.PropertyType)) return value => ToDouble(property.GetValue(value));

        MethodInfo method = type.GetMethod("Length", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (method != null && TypeRules.IsNumeric(method.ReturnType)) return value => ToDouble(method.Invoke(value, null));
        return null;
    }
}
