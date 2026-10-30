using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UIConstraints.Core.Data;

namespace UIConstraints.Core.Parsing.Expressions {
// Priorities follow C#: ^ (power here, not xor) before * / % before + -
public abstract class CalculationExpression : BinaryExpression {
	// the methods C# compiles operator overloads to (^ is xor in C#, so there is no operator for power)
	private static readonly Dictionary<char, string> OPERATOR_METHODS = new Dictionary<char, string> {
		{ '+', "op_Addition" }, { '-', "op_Subtraction" }, { '*', "op_Multiply" }, { '/', "op_Division" }, { '%', "op_Modulus" }
	};

	private static readonly HashSet<Type> SIGNED_INTEGERS = new HashSet<Type> { typeof(sbyte), typeof(short), typeof(int), typeof(long) };

	[CanBeNull] private Type resultType;
	[CanBeNull] private MethodCall userOperator; // an operator the operand types define (e.g. Vector2 * float)

	protected abstract char Operation { get; }

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);
		Type leftType = Left.GetExpectedType();
		Type rightType = Right.GetExpectedType();
		if (leftType == null || rightType == null || DescribeNonValue(Left) != null || DescribeNonValue(Right) != null) return;

		// like in C#, number literals that fit the type of the other operand take its type, so ulongValue + 1 is not an error (1 is an int, which can't be mixed with ulong)
		leftType = GetConstantType(Left, rightType) ?? leftType;
		rightType = GetConstantType(Right, leftType) ?? rightType;
		resultType = ResolveResultType(Operation, leftType, rightType, out userOperator);
	}

	// the unsigned type of the other operand, if the expression is a non-negative integer literal
	[CanBeNull]
	internal static Type GetConstantType(Expression expression, Type otherType) {
		if (otherType != typeof(uint) && otherType != typeof(ulong)) return null;
		return expression is NumberExpression { Value: int or long } number && Convert.ToInt64(number.Value) >= 0 ? otherType : null;
	}

	/**
	 * The type of the result of an operation on values of the given types, null if the operator can't be applied to them.
	 * Numbers and string concatenation are built in, other types can define operators (like op_Multiply for Vector2 * float), in which case userOperator is set.
	 */
	[CanBeNull]
	public static Type ResolveResultType(char operation, Type leftType, Type rightType, [CanBeNull] out MethodCall userOperator) {
		userOperator = null;
		if (operation == '+' && (leftType == typeof(string) || rightType == typeof(string))) return typeof(string); // string concatenation like in C#

		if (TypeRules.IsNumeric(leftType) && TypeRules.IsNumeric(rightType)) {
			if (operation == '^') return leftType == typeof(decimal) || rightType == typeof(decimal) ? null : typeof(double); // Math.Pow, which only exists for doubles
			return GetPromotedType(leftType, rightType);
		}

		if (!OPERATOR_METHODS.TryGetValue(operation, out string methodName)) return null;
		foreach (Type declaringType in new[] { leftType, rightType }) {
			if (declaringType == TypeRules.NULL) continue;
			userOperator = MemberResolver.ResolveMethod(declaringType, methodName, true, new[] { leftType, rightType }, out _);
			if (userOperator != null) return userOperator.ReturnType;
		}

		return null;
	}

	/**
	 * The type C# calculates in for two numeric operands (binary numeric promotion), null if they can't be mixed:
	 * decimal can't be mixed with float or double, and ulong can't be mixed with signed integers (as neither type can hold all values of the other).
	 * Otherwise the result is the "bigger" type, at least int, and uint mixed with signed integers becomes long.
	 */
	[CanBeNull]
	public static Type GetPromotedType(Type a, Type b) {
		if (a == typeof(decimal) || b == typeof(decimal)) return IsFloatingPoint(a) || IsFloatingPoint(b) ? null : typeof(decimal);
		if (a == typeof(double) || b == typeof(double)) return typeof(double);
		if (a == typeof(float) || b == typeof(float)) return typeof(float);
		if (a == typeof(ulong) || b == typeof(ulong)) return SIGNED_INTEGERS.Contains(a) || SIGNED_INTEGERS.Contains(b) ? null : typeof(ulong);
		if (a == typeof(long) || b == typeof(long)) return typeof(long);
		if (a == typeof(uint) || b == typeof(uint)) return SIGNED_INTEGERS.Contains(a) || SIGNED_INTEGERS.Contains(b) ? typeof(long) : typeof(uint);
		return typeof(int);
	}

	private static bool IsFloatingPoint(Type type) {
		return type == typeof(float) || type == typeof(double);
	}

	public override Type GetExpectedType() {
		return resultType;
	}

	public override void Validate(ParseResult parseResult) {
		bool leftValid = CheckValue(Left, parseResult);
		bool rightValid = CheckValue(Right, parseResult);
		if (leftValid && rightValid && resultType == null)
			AddError(parseResult, $"Operator '{Operation}' cannot be applied to operands of type {TypeNames.GetDisplayName(Left.GetExpectedType())} and {TypeNames.GetDisplayName(Right.GetExpectedType())}");
	}

	public override object Evaluate(DataContext context) {
		object left = Left.Evaluate(context);
		object right = Right.Evaluate(context);
		return PerformOperation(left, right, Operation, userOperator, resultType, this);
	}

	// resultType and userOperator: as resolved by ResolveResultType
	public static object PerformOperation(object a, object b, char operation, [CanBeNull] MethodCall userOperator, Type resultType, Expression expressionRef) {
		if (userOperator != null) return MemberResolver.Invoke(userOperator, null, new[] { a, b }, expressionRef);
		if (resultType == typeof(string)) return string.Concat(a, b); // string concatenation like in C#
		if (a == null || b == null) throw new EvaluationException($"Cannot perform operation '{operation}' on null", expressionRef);

		try {
			if (operation == '^') return Math.Pow(Convert.ToDouble(a), Convert.ToDouble(b));

			// the operands are converted to the result type, which can hold their values (that is what the promotion rules make sure of)
			if (resultType == typeof(int)) return Calculate(Convert.ToInt32(a), Convert.ToInt32(b), operation);
			if (resultType == typeof(uint)) return Calculate(Convert.ToUInt32(a), Convert.ToUInt32(b), operation);
			if (resultType == typeof(long)) return Calculate(Convert.ToInt64(a), Convert.ToInt64(b), operation);
			if (resultType == typeof(ulong)) return Calculate(Convert.ToUInt64(a), Convert.ToUInt64(b), operation);
			if (resultType == typeof(float)) return Calculate(Convert.ToSingle(a), Convert.ToSingle(b), operation);
			if (resultType == typeof(double)) return Calculate(Convert.ToDouble(a), Convert.ToDouble(b), operation);
			if (resultType == typeof(decimal)) return Calculate(Convert.ToDecimal(a), Convert.ToDecimal(b), operation);
		}
		catch (DivideByZeroException e) {
			throw new EvaluationException("Division by zero", expressionRef, e);
		}
		catch (OverflowException e) {
			throw new EvaluationException($"The result of '{operation}' is too big for {TypeNames.GetDisplayName(resultType)}", expressionRef, e);
		}

		throw new EvaluationException($"Operator '{operation}' can't be applied to {TypeNames.GetDisplayName(a.GetType())} and {TypeNames.GetDisplayName(b.GetType())}", expressionRef);
	}

	// would have really appreciated a common supertype for all numeric types to avoid these copies
	private static int Calculate(int a, int b, char operation) {
		return operation switch {
			'+' => a + b,
			'-' => a - b,
			'*' => a * b,
			'/' => a / b,
			'%' => a % b,
			_ => throw new NotImplementedException($"Unimplemented operator {operation} for Mathematical operation.")
		};
	}

	private static uint Calculate(uint a, uint b, char operation) {
		return operation switch {
			'+' => a + b,
			'-' => a - b,
			'*' => a * b,
			'/' => a / b,
			'%' => a % b,
			_ => throw new NotImplementedException($"Unimplemented operator {operation} for Mathematical operation.")
		};
	}

	private static long Calculate(long a, long b, char operation) {
		return operation switch {
			'+' => a + b,
			'-' => a - b,
			'*' => a * b,
			'/' => a / b,
			'%' => a % b,
			_ => throw new NotImplementedException($"Unimplemented operator {operation} for Mathematical operation.")
		};
	}

	private static ulong Calculate(ulong a, ulong b, char operation) {
		return operation switch {
			'+' => a + b,
			'-' => a - b,
			'*' => a * b,
			'/' => a / b,
			'%' => a % b,
			_ => throw new NotImplementedException($"Unimplemented operator {operation} for Mathematical operation.")
		};
	}

	private static float Calculate(float a, float b, char operation) {
		return operation switch {
			'+' => a + b,
			'-' => a - b,
			'*' => a * b,
			'/' => a / b,
			'%' => a % b,
			_ => throw new NotImplementedException($"Unimplemented operator {operation} for Mathematical operation.")
		};
	}

	private static double Calculate(double a, double b, char operation) {
		return operation switch {
			'+' => a + b,
			'-' => a - b,
			'*' => a * b,
			'/' => a / b,
			'%' => a % b,
			_ => throw new NotImplementedException($"Unimplemented operator {operation} for Mathematical operation.")
		};
	}

	// decimal arithmetic is checked (it throws instead of overflowing), unlike that of the other types
	private static decimal Calculate(decimal a, decimal b, char operation) {
		return operation switch {
			'+' => a + b,
			'-' => a - b,
			'*' => a * b,
			'/' => a / b,
			'%' => a % b,
			_ => throw new NotImplementedException($"Unimplemented operator {operation} for Mathematical operation.")
		};
	}
}

[ExpressionPattern(typeof(Expression), @"\+", typeof(Expression))]
[PatternPriority(-3)]
public class AdditionExpression : CalculationExpression {
	protected override char Operation => '+';
}

[ExpressionPattern(typeof(Expression), @"-", typeof(Expression))]
[PatternPriority(-3)]
public class SubtractionExpression : CalculationExpression {
	protected override char Operation => '-';
}

[ExpressionPattern(typeof(Expression), @"\*", typeof(Expression))]
[PatternPriority(-2)]
public class MultiplicationExpression : CalculationExpression {
	protected override char Operation => '*';
}

[ExpressionPattern(typeof(Expression), @"\/", typeof(Expression))]
[PatternPriority(-2)]
public class DivisionExpression : CalculationExpression {
	protected override char Operation => '/';
}

[ExpressionPattern(typeof(Expression), @"%", typeof(Expression))]
[PatternPriority(-2)]
public class ModuloExpression : CalculationExpression {
	protected override char Operation => '%';
}

[ExpressionPattern(typeof(Expression), @"\^", typeof(Expression))]
[PatternPriority(-1)]
public class PowerExpression : CalculationExpression {
	protected override char Operation => '^';
}

/**
 * -x. A minus following a value is a subtraction instead, which the negative lookbehind checks (in placeholders, values start with $).
 * Like the other unary operators, it binds tighter than any binary operator, but member access is resolved first, so -a.b is -(a.b).
 */
[ExpressionPattern(@"(?<!\$\S*(?:\x20W)*\x20)-", typeof(Expression))]
[PatternPriority(0)]
public class NegationExpression : Expression {
	[CanBeNull] private Type resultType;
	[CanBeNull] private MethodCall userOperator; // an operator the operand type defines (e.g. -Vector2.one)

	public Expression Operand => (Expression)args[0];

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);
		Type operandType = Operand.GetExpectedType();
		if (operandType == null || operandType == TypeRules.NULL || DescribeNonValue(Operand) != null) return;

		// like 0 - x in C#: at least int, uint becomes long, and ulong can't be negated
		if (TypeRules.IsNumeric(operandType)) {
			resultType = operandType == typeof(ulong) ? null : CalculationExpression.GetPromotedType(typeof(int), operandType);
			return;
		}

		userOperator = MemberResolver.ResolveMethod(operandType, "op_UnaryNegation", true, new[] { operandType }, out _);
		resultType = userOperator?.ReturnType;
	}

	public override Type GetExpectedType() {
		return resultType;
	}

	public override void Validate(ParseResult parseResult) {
		if (CheckValue(Operand, parseResult) && resultType == null) AddError(parseResult, $"Operator '-' cannot be applied to an operand of type {TypeNames.GetDisplayName(Operand.GetExpectedType())}");
	}

	public override object Evaluate(DataContext context) {
		object value = Operand.Evaluate(context);
		if (userOperator != null) return MemberResolver.Invoke(userOperator, null, new[] { value }, this);
		return CalculationExpression.PerformOperation(0, value, '-', null, resultType, this);
	}
}
}
