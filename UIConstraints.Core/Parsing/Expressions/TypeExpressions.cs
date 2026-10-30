using System;
using System.Linq;
using JetBrains.Annotations;
using UIConstraints.Core.Data;

namespace UIConstraints.Core.Parsing.Expressions {
// Expressions that name a type. They are not values, so they can't be evaluated, but other expressions use them (e.g. casts or static member access).

// built-in types written as keywords, e.g. int (which is System.Int32)
[ExpressionPattern(@"(?<!\S)(?:object|string|bool|char|byte|sbyte|short|ushort|int|uint|long|ulong|float|double|decimal)(?!\S)")]
[PatternPriority(999)]
public class TypeKeywordExpression : Expression {
	public string Keyword => ((IdentifierToken)args[0]).Identifier;

	public override Type GetReferencedType() {
		return TypeNames.GetKeywordType(Keyword);
	}

	public override object Evaluate(DataContext context) {
		throw new EvaluationException($"'{Keyword}' is a type and cannot be used as a value", this);
	}
}

/**
 * A generic type with type arguments, e.g. List<int> or Dictionary<string, List<int>>.
 * As < and > are also comparison operators, this follows the rule C# uses to tell them apart: it is only a generic if the closing '>' is followed by something a type can be followed by
 * (e.g. '(', ')' or '.'), but not something a value can be compared to. 'a < b > c' and 'F(a < b, c > d)' are comparisons, as they can't be types.
 * This has the priority of member access, so it is resolved before comparisons and can be part of names (e.g. Comparer<int>.Default).
 * The lookahead does not use spaces, as those would be replaced by the pattern attribute (W is the placeholder of whitespace tokens).
 * A '(' may already be part of a BracketExpression (e.g. in new List<int>(capacity)), which counts as '(' too.
 */
[ExpressionPattern(typeof(NameExpression), @" < ", typeof(Expression), @"(?: , ", typeof(Expression), @")* >" + TYPE_FOLLOWER_LOOKAHEAD, AutoSpace = false)]
[PatternPriority(2)]
public class GenericTypeExpression : Expression {
	private const string TYPE_FOLLOWER_LOOKAHEAD = @"(?=(?:\x20W)*(?:$|\x20(?:\(|\$\S*\.BracketExpression|\)|\]|\[|\.|,|>|\?|:|;|==|!=|&&|\|\||&|\||\^)(?:\x20|$)))";

	[CanBeNull] private Type constructedType;
	[CanBeNull] private string constructionError;
	private bool isMethodName; // the name and type arguments of a called generic method (e.g. GetComponent<Image> in x.GetComponent<Image>()), not a type

	public NameExpression GenericName => (NameExpression)args[0];
	public Expression[] TypeArguments { get; private set; }

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);
		TypeArguments = args.Skip(1).Cast<Expression>().ToArray();
		GenericName.ResolveAsGenericType(TypeArguments.Length);

		Type definition = GenericName.GetReferencedType();
		Type[] typeArguments = TypeArguments.Select(t => t.GetReferencedType()).ToArray();
		if (definition == null || typeArguments.Any(t => t == null)) return;

		try {
			constructedType = definition.MakeGenericType(typeArguments);
		}
		catch (ArgumentException) {
			constructionError = $"The type arguments ({string.Join(", ", typeArguments.Select(TypeNames.GetDisplayName))}) do not fulfill the constraints of {TypeNames.GetDisplayName(definition)}";
		}
	}

	// marks this as the name of a called generic method (see FunctionCallExpression), which validates the method itself
	public void MarkAsMethodName() {
		isMethodName = true;
		constructedType = null;
	}

	public override Type GetReferencedType() {
		return constructedType;
	}

	public override void Validate(ParseResult parseResult) {
		// an unknown generic type is reported by the name itself
		foreach (Expression typeArgument in TypeArguments) CheckType(typeArgument, parseResult);
		if (constructionError != null && !isMethodName) AddError(parseResult, constructionError);
	}

	public override object Evaluate(DataContext context) {
		throw new EvaluationException("A type cannot be used as a value", this);
	}
}

// an array type, e.g. int[], or jagged ones like int[][]. Multidimensional ones (int[,]) have a class per rank below, as the commas are not part of the matched elements
[ExpressionPattern(typeof(Expression), @"\[", @"\]")]
[PatternPriority(2)]
public class ArrayTypeExpression : Expression {
	public Expression ElementType => (Expression)args[0];

	protected virtual int Rank => 1;

	public override Type GetReferencedType() {
		Type elementType = ElementType.GetReferencedType();
		if (elementType == null) return null;
		return Rank == 1 ? elementType.MakeArrayType() : elementType.MakeArrayType(Rank); // MakeArrayType(1) would be an array with a lower bound, which C# can't declare
	}

	public override void Validate(ParseResult parseResult) {
		CheckType(ElementType, parseResult);
	}

	public override object Evaluate(DataContext context) {
		throw new EvaluationException("A type cannot be used as a value", this);
	}
}

[ExpressionPattern(typeof(Expression), @"\[", ",", @"\]")]
[PatternPriority(2)]
public class ArrayType2Expression : ArrayTypeExpression {
	protected override int Rank => 2;
}

[ExpressionPattern(typeof(Expression), @"\[", ",", ",", @"\]")]
[PatternPriority(2)]
public class ArrayType3Expression : ArrayTypeExpression {
	protected override int Rank => 3;
}

/**
 * T?, which is Nullable<T> for value types (e.g. int?). For reference types it is the type itself, like in C# (where it only affects warnings).
 * As ? also starts conditional expressions (a ? b : c), it is only a nullable type if it is followed by something a type can be followed by, but the second part of a conditional can't:
 * the end, ')' (e.g. in the cast (int?)x), ',' or '>' (in type arguments), ';', or empty brackets (int?[]). The brackets have to be empty, as cond ? [1] : [2] is a conditional.
 */
[ExpressionPattern(typeof(Expression), @" \?(?=(?:\x20W)*(?:$|\x20(?:\)|,|>|;|\[(?:\x20(?:W|,))*\x20\])(?:\x20|$)))", AutoSpace = false)]
[PatternPriority(2)]
public class NullableTypeExpression : Expression {
	public Expression UnderlyingType => (Expression)args[0];

	public override Type GetReferencedType() {
		Type type = UnderlyingType.GetReferencedType();
		if (type == null || !type.IsValueType || Nullable.GetUnderlyingType(type) != null) return type;
		return typeof(Nullable<>).MakeGenericType(type);
	}

	public override void Validate(ParseResult parseResult) {
		CheckType(UnderlyingType, parseResult);
	}

	public override object Evaluate(DataContext context) {
		throw new EvaluationException("A type cannot be used as a value", this);
	}
}
}
