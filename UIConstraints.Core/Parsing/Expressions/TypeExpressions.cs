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
 * (e.g. ')' or '.'), but not something a value can be compared to. 'a < b > c' and 'F(a < b, c > d)' are comparisons, as they can't be types.
 * This has the priority of member access, so it is resolved before comparisons and can be part of names (e.g. Comparer<int>.Default).
 * The lookahead does not use spaces, as those would be replaced by the pattern attribute (W is the placeholder of whitespace tokens).
 */
[ExpressionPattern(typeof(NameExpression), @" < ", typeof(Expression), @"(?: , ", typeof(Expression), @")* >" + TYPE_FOLLOWER_LOOKAHEAD, AutoSpace = false)]
[PatternPriority(2)]
public class GenericTypeExpression : Expression {
	private const string TYPE_FOLLOWER_LOOKAHEAD = @"(?=(?:\x20W)*(?:$|\x20(?:\)|\]|\[|\.|,|>|\?|:|==|!=|&&|\|\||&|\||\^)(?:\x20|$)))";

	[CanBeNull] private Type constructedType;
	[CanBeNull] private string constructionError;

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

	public override Type GetReferencedType() {
		return constructedType;
	}

	public override void Validate(ParseResult parseResult) {
		// an unknown generic type is reported by the name itself
		foreach (Expression typeArgument in TypeArguments) CheckType(typeArgument, parseResult);
		if (constructionError != null) AddError(parseResult, constructionError);
	}

	public override object Evaluate(DataContext context) {
		throw new EvaluationException("A type cannot be used as a value", this);
	}
}

// an array type, e.g. int[] (multidimensional arrays are not supported, jagged ones like int[][] are)
[ExpressionPattern(typeof(Expression), @"\[", @"\]")]
[PatternPriority(2)]
public class ArrayTypeExpression : Expression {
	public Expression ElementType => (Expression)args[0];

	public override Type GetReferencedType() {
		return ElementType.GetReferencedType()?.MakeArrayType();
	}

	public override void Validate(ParseResult parseResult) {
		CheckType(ElementType, parseResult);
	}

	public override object Evaluate(DataContext context) {
		throw new EvaluationException("A type cannot be used as a value", this);
	}
}
}
