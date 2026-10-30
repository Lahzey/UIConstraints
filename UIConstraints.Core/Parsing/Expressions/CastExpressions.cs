using System;
using UIConstraints.Core.Data;

namespace UIConstraints.Core.Parsing.Expressions;

/**
 * A cast like (int)x or (List<Item>)x: a bracket containing a type, followed by a value. A bracket followed directly by a value is not valid otherwise,
 * so there is no ambiguity with brackets around values ('(a) - b' is a subtraction, as there is an operator in between).
 * The priority is below member access (so (int)a.b casts a.b, like in C#), but above the other unary operators.
 * The negative lookahead makes chained casts right associative: in (A)(B)x, (B)x is cast first, as (A)(B) is not followed by an operator.
 */
[ExpressionPattern(typeof(BracketExpression), " ", typeof(Expression), @"(?!(?:\x20W)*\x20(?:\$|\(|!(?=\x20|$)))", AutoSpace = false)]
[PatternPriority(1)]
public class CastExpression : Expression {
    public BracketExpression TargetType => (BracketExpression)args[0];
    public Expression Value => (Expression)args[1];

    public override Type GetExpectedType() {
        return TargetType.GetReferencedType();
    }

    public override void Validate(ParseResult parseResult) {
        bool typeValid = TargetType.GetReferencedType() != null;
        if (!typeValid && TargetType.Content.GetReferencedNamespace() == null && TargetType.Content.GetExpectedType() != null) TargetType.AddError(parseResult, "Only a type can be followed by a value (to cast it)");
        else if (!typeValid) CheckType(TargetType.Content, parseResult);

        if (!CheckValue(Value, parseResult) || !typeValid) return;
        Type from = Value.GetExpectedType();
        Type to = TargetType.GetReferencedType();
        if (!TypeRules.IsExplicitlyConvertible(from, to)) AddError(parseResult, $"Cannot convert {TypeNames.GetDisplayName(from)} to {TypeNames.GetDisplayName(to)}");
    }

    public override object Evaluate(DataContext context) {
        return TypeRules.Cast(Value.Evaluate(context), TargetType.GetReferencedType(), this);
    }
}

// x is T, true if x is not null and its type is T or extends T
[ExpressionPattern(typeof(Expression), "is", typeof(Expression))]
[PatternPriority(-100)]
public class IsExpression : Expression {
    public Expression Left => (Expression)args[0];
    public Expression Right => (Expression)args[2]; // args[1] is the keyword token, which is kept like all identifier tokens

    public override Type GetExpectedType() {
        return typeof(bool);
    }

    public override void Validate(ParseResult parseResult) {
        CheckValue(Left, parseResult);
        CheckType(Right, parseResult);
    }

    public override object Evaluate(DataContext context) {
        object value = Left.Evaluate(context);
        return value != null && Right.GetReferencedType().IsInstanceOfType(value);
    }
}

// x as T, x if it is a T (or extends T), null otherwise
[ExpressionPattern(typeof(Expression), "as", typeof(Expression))]
[PatternPriority(-100)]
public class AsExpression : Expression {
    public Expression Left => (Expression)args[0];
    public Expression Right => (Expression)args[2]; // args[1] is the keyword token, which is kept like all identifier tokens

    public override Type GetExpectedType() {
        return Right.GetReferencedType();
    }

    public override void Validate(ParseResult parseResult) {
        bool valueValid = CheckValue(Left, parseResult);
        if (!CheckType(Right, parseResult)) return;

        Type to = Right.GetReferencedType();
        if (!TypeRules.CanBeNull(to)) {
            Right.AddError(parseResult, $"'as' needs a type that can be null, but {TypeNames.GetDisplayName(to)} is a value type (use a cast instead)");
            return;
        }

        if (valueValid && !TypeRules.IsReferenceConvertible(Left.GetExpectedType(), to)) AddError(parseResult, $"{TypeNames.GetDisplayName(Left.GetExpectedType())} can never be converted to {TypeNames.GetDisplayName(to)} with 'as'");
    }

    public override object Evaluate(DataContext context) {
        object value = Left.Evaluate(context);
        Type type = Right.GetReferencedType();
        return value != null && (Nullable.GetUnderlyingType(type) ?? type).IsInstanceOfType(value) ? value : null;
    }
}