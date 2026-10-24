using System;
using JetBrains.Annotations;
using UIConstraints.Core.Data;

namespace UIConstraints.Core.Parsing.Expressions {
// Priorities follow C#: ^ (power here, not xor) before * / % before + -
public abstract class CalculationExpression : BinaryExpression {
	// the numeric types calculations are performed in, by level (smaller integer types are promoted to int like in C#, and yes, long * float = float, not double)
	private static readonly Type[] LEVEL_TYPES = { typeof(int), typeof(long), typeof(float), typeof(double) };

	[CanBeNull] private Type resultType;

	protected abstract char Operation { get; }

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);
		Type leftType = Left.GetExpectedType();
		Type rightType = Right.GetExpectedType();
		if (leftType == null || rightType == null || DescribeNonValue(Left) != null || DescribeNonValue(Right) != null) return;

		if (Operation == '+' && (leftType == typeof(string) || rightType == typeof(string))) {
			resultType = typeof(string); // string concatenation like in C#
			return;
		}

		int leftLevel = GetLevel(leftType);
		int rightLevel = GetLevel(rightType);
		if (leftLevel < 0 || rightLevel < 0) return;
		resultType = Operation == '^' ? typeof(double) : LEVEL_TYPES[Math.Max(leftLevel, rightLevel)];
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
		return PerformOperation(left, right, Operation, this);
	}

	public static object PerformOperation(object a, object b, char operation, Expression expressionRef) {
		if (operation == '+' && (a is string || b is string)) return string.Concat(a, b); // string concatenation like in C#

		int level = Math.Max(GetLevel(a, operation, expressionRef), GetLevel(b, operation, expressionRef));
		try {
			if (operation == '^') return Math.Pow(Convert.ToDouble(a), Convert.ToDouble(b)); // math.pow only accepts doubles, so no need to check level

			switch (level) {
				case 0:
					return Calculate(Convert.ToInt32(a), Convert.ToInt32(b), operation);
				case 1:
					return Calculate(Convert.ToInt64(a), Convert.ToInt64(b), operation);
				case 2:
					return Calculate(Convert.ToSingle(a), Convert.ToSingle(b), operation);
				case 3:
					return Calculate(Convert.ToDouble(a), Convert.ToDouble(b), operation);
				default:
					throw new NotImplementedException($"Unimplemented level {level} for Mathematical operation.");
			}
		}
		catch (DivideByZeroException e) {
			throw new EvaluationException("Division by zero", expressionRef, e);
		}
	}

	private static int GetLevel(object o, char operation, Expression expressionRef) {
		if (o == null) throw new EvaluationException($"Cannot perform operation '{operation}' on null", expressionRef);
		int level = GetLevel(o.GetType());
		if (level < 0) throw new EvaluationException($"Unsupported type for operation '{operation}': {o.GetType().FullName}", expressionRef);
		return level;
	}

	// returns -1 for unsupported types
	private static int GetLevel(Type type) {
		if (type == typeof(int) || type == typeof(short) || type == typeof(ushort) || type == typeof(byte) || type == typeof(sbyte) || type == typeof(char)) return 0;
		if (type == typeof(long) || type == typeof(uint)) return 1;
		if (type == typeof(float)) return 2;
		if (type == typeof(double)) return 3;
		return -1;
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
}
