using System;
using UIConstraints.Core.Data;

// Priorities follow C#: ! before relational (and is / as) before equality before & before | before && before ||
namespace UIConstraints.Core.Parsing.Expressions;

// < > <= >= on numbers
public abstract class RelationalExpression : BinaryExpression {
    protected abstract string Operator { get; }

    protected abstract bool Compare(double left, double right);

    public override Type GetExpectedType() {
        return typeof(bool);
    }

    public override void Validate(ParseResult parseResult) {
        bool leftValid = CheckValue(Left, parseResult);
        bool rightValid = CheckValue(Right, parseResult);
        if (leftValid && rightValid && (!TypeRules.IsNumeric(Left.GetExpectedType()) || !TypeRules.IsNumeric(Right.GetExpectedType())))
            AddError(parseResult, $"Operator '{Operator}' cannot be applied to operands of type {TypeNames.GetDisplayName(Left.GetExpectedType())} and {TypeNames.GetDisplayName(Right.GetExpectedType())}");
    }

    public override object Evaluate(DataContext context) {
        object left = Left.Evaluate(context);
        object right = Right.Evaluate(context);
        if (left == null || right == null) throw new EvaluationException($"Cannot perform {Operator} on null", this);
        return Compare(Convert.ToDouble(left), Convert.ToDouble(right));
    }
}

// == and !=
public abstract class EqualityExpression : BinaryExpression {
    protected abstract string Operator { get; }

    public override Type GetExpectedType() {
        return typeof(bool);
    }

    public override void Validate(ParseResult parseResult) {
        bool leftValid = CheckValue(Left, parseResult);
        bool rightValid = CheckValue(Right, parseResult);
        if (leftValid && rightValid && !TypeRules.IsEqualityComparable(Left.GetExpectedType(), Right.GetExpectedType()))
            AddError(parseResult, $"Operator '{Operator}' cannot be applied to operands of type {TypeNames.GetDisplayName(Left.GetExpectedType())} and {TypeNames.GetDisplayName(Right.GetExpectedType())}");
    }

    // values are boxed, so == would compare references. Numbers are compared by value even if their types differ (e.g. 1 == 1.0f)
    public static bool AreEqual(object left, object right) {
        if (left != null && right != null && TypeRules.IsNumeric(left.GetType()) && TypeRules.IsNumeric(right.GetType()))
            return Convert.ToDouble(left) == Convert.ToDouble(right);
        return Equals(left, right);
    }
}

// & | && || on bools
public abstract class BooleanOperationExpression : BinaryExpression {
    protected abstract string Operator { get; }

    public override Type GetExpectedType() {
        return typeof(bool);
    }

    public override void Validate(ParseResult parseResult) {
        bool leftValid = CheckValue(Left, parseResult);
        bool rightValid = CheckValue(Right, parseResult);
        if (leftValid && rightValid && (Left.GetExpectedType() != typeof(bool) || Right.GetExpectedType() != typeof(bool)))
            AddError(parseResult, $"Operator '{Operator}' cannot be applied to operands of type {TypeNames.GetDisplayName(Left.GetExpectedType())} and {TypeNames.GetDisplayName(Right.GetExpectedType())}");
    }

    protected bool EvaluateOperand(Expression operand, DataContext context) {
        if (operand.Evaluate(context) is bool value) return value;
        throw new EvaluationException($"Cannot perform {Operator} on non-boolean values", this);
    }
}

[ExpressionPattern(typeof(Expression), @"==", typeof(Expression))]
[PatternPriority(-101)]
public class EqualsExpression : EqualityExpression {
    protected override string Operator => "==";

    public override object Evaluate(DataContext context) {
        return AreEqual(Left.Evaluate(context), Right.Evaluate(context));
    }
}

[ExpressionPattern(typeof(Expression), @"!=", typeof(Expression))]
[PatternPriority(-101)]
public class NotEqualsExpression : EqualityExpression {
    protected override string Operator => "!=";

    public override object Evaluate(DataContext context) {
        return !AreEqual(Left.Evaluate(context), Right.Evaluate(context));
    }
}

[ExpressionPattern(typeof(Expression), @">", typeof(Expression))]
[PatternPriority(-100)]
public class GreaterThanExpression : RelationalExpression {
    protected override string Operator => ">";

    protected override bool Compare(double left, double right) {
        return left > right;
    }
}

[ExpressionPattern(typeof(Expression), @">=", typeof(Expression))]
[PatternPriority(-100)]
public class GreaterThanOrEqualExpression : RelationalExpression {
    protected override string Operator => ">=";

    protected override bool Compare(double left, double right) {
        return left >= right;
    }
}

[ExpressionPattern(typeof(Expression), @"<", typeof(Expression))]
[PatternPriority(-100)]
public class SmallerThanExpression : RelationalExpression {
    protected override string Operator => "<";

    protected override bool Compare(double left, double right) {
        return left < right;
    }
}

[ExpressionPattern(typeof(Expression), @"<=", typeof(Expression))]
[PatternPriority(-100)]
public class SmallerThanOrEqualExpression : RelationalExpression {
    protected override string Operator => "<=";

    protected override bool Compare(double left, double right) {
        return left <= right;
    }
}

[ExpressionPattern(typeof(Expression), @"&", typeof(Expression))]
[PatternPriority(-102)]
public class AndExpression : BooleanOperationExpression {
    protected override string Operator => "&";

    public override object Evaluate(DataContext context) {
        return EvaluateOperand(Left, context) & EvaluateOperand(Right, context);
    }
}

[ExpressionPattern(typeof(Expression), @"&&", typeof(Expression))]
[PatternPriority(-104)]
public class LogicalAndExpression : BooleanOperationExpression {
    protected override string Operator => "&&";

    public override object Evaluate(DataContext context) {
        return EvaluateOperand(Left, context) && EvaluateOperand(Right, context);
    }
}

[ExpressionPattern(typeof(Expression), @"\|", typeof(Expression))]
[PatternPriority(-103)]
public class OrExpression : BooleanOperationExpression {
    protected override string Operator => "|";

    public override object Evaluate(DataContext context) {
        return EvaluateOperand(Left, context) | EvaluateOperand(Right, context);
    }
}

[ExpressionPattern(typeof(Expression), @"\|\|", typeof(Expression))]
[PatternPriority(-105)]
public class LogicalOrExpression : BooleanOperationExpression {
    protected override string Operator => "||";

    public override object Evaluate(DataContext context) {
        return EvaluateOperand(Left, context) || EvaluateOperand(Right, context);
    }
}

// unary operators bind tighter than any binary operator, but member access and function calls are resolved first, so !a.b is !(a.b)
[ExpressionPattern(@"!", typeof(Expression))]
[PatternPriority(0)]
public class NotExpression : Expression {
    public Expression Expression => (Expression)args[0];

    public override Type GetExpectedType() {
        return typeof(bool);
    }

    public override void Validate(ParseResult parseResult) {
        if (CheckValue(Expression, parseResult) && Expression.GetExpectedType() != typeof(bool))
            AddError(parseResult, $"Operator '!' cannot be applied to an operand of type {TypeNames.GetDisplayName(Expression.GetExpectedType())}");
    }

    public override object Evaluate(DataContext context) {
        object value = Expression.Evaluate(context);
        if (!(value is bool))
            throw new EvaluationException("Cannot perform NOT on non-boolean values", this);
        return !(bool)value;
    }
}