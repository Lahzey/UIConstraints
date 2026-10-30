using System;
using System.Linq;
using JetBrains.Annotations;
using UIConstraints.Core.Data;

namespace UIConstraints.Core.Parsing.Expressions {
/**
 * new T(arguments), e.g. new Vector2(1, 2) or new List<int>().
 * The arguments are usually matched as a list, but a single argument after a type that is not a name (e.g. new List<int>(capacity)) is already a BracketExpression by then,
 * as BracketExpression only excludes parentheses directly after names. The second pattern handles that case.
 * The priority is above member access, so new A.B() is resolved once A.B is, but before .C in new A.B().C (like in C#).
 */
[ExpressionPattern(@"(?<!\S)new ", typeof(Expression), @" \((?: ", typeof(Expression), @"(?: , ", typeof(Expression), @")*)? \)", AutoSpace = false)]
[ExpressionPattern(@"(?<!\S)new", typeof(Expression), typeof(BracketExpression))]
[PatternPriority(3)]
public class ObjectCreationExpression : Expression {
	[CanBeNull] private MethodCall constructor;
	[CanBeNull] private string resolveError;

	public Expression TypeExpression => (Expression)args[1]; // args[0] is the keyword token, which is kept like all identifier tokens
	public Expression[] Arguments { get; private set; }

	[CanBeNull] private Type CreatedType => TypeExpression.GetReferencedType();

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);
		Arguments = args.Length == 3 && args[2] is BracketExpression bracket ? new[] { bracket.Content } : args.Skip(2).Cast<Expression>().ToArray();

		Type type = CreatedType;
		if (type == null || type.IsAbstract || type.IsInterface || type.ContainsGenericParameters) return; // reported by Validate
		if (Arguments.Any(a => a.GetExpectedType() == null || DescribeNonValue(a) != null)) return;
		if (type.IsValueType && Arguments.Length == 0) return; // structs always have a parameterless constructor, which reflection does not list

		constructor = MemberResolver.ResolveConstructor(type, Arguments.Select(a => a.GetExpectedType()).ToArray(), out resolveError);
	}

	public override Type GetExpectedType() {
		Type type = CreatedType;
		if (type == null) return null;
		return constructor != null || (type.IsValueType && Arguments.Length == 0) ? type : null;
	}

	public override void Validate(ParseResult parseResult) {
		bool argumentsValid = true;
		foreach (Expression argument in Arguments) argumentsValid &= CheckValue(argument, parseResult);
		if (!CheckType(TypeExpression, parseResult)) return;

		Type type = CreatedType;
		string typeName = TypeNames.GetDisplayName(type);
		if (type.IsInterface || type.IsAbstract) AddError(parseResult, $"Cannot create an instance of {typeName}, as it is {(type.IsInterface ? "an interface" : type.IsSealed ? "static" : "abstract")}");
		else if (argumentsValid && GetExpectedType() == null) AddError(parseResult, resolveError ?? $"{typeName} has no public constructor");
	}

	public override object Evaluate(DataContext context) {
		Type type = CreatedType;
		if (constructor == null) {
			if (type != null && type.IsValueType && Arguments.Length == 0) return Activator.CreateInstance(type);
			throw new EvaluationException("No fitting constructor was found", this);
		}

		object[] values = Arguments.Select(a => a.Evaluate(context)).ToArray();
		return MemberResolver.Invoke(constructor, null, values, this);
	}
}

/**
 * new T[length], e.g. new int[3], or new int[2, 3] for a multidimensional array, with default elements (arrays with elements are written as collections, e.g. [1, 2, 3]).
 * Like object creation, the priority is above member access, so new T[3].Length is the length of the new array.
 */
[ExpressionPattern(@"(?<!\S)new ", typeof(Expression), @" \[ ", typeof(Expression), @"(?: , ", typeof(Expression), @")* \]", AutoSpace = false)]
[PatternPriority(3)]
public class ArrayCreationExpression : Expression {
	public Expression ElementTypeExpression => (Expression)args[1]; // args[0] is the keyword token, which is kept like all identifier tokens
	public Expression[] Lengths { get; private set; }

	// whether the elements are nullable (new int?[3]), as the ? can't be a NullableTypeExpression there (see NullableArrayCreationExpression)
	protected virtual bool NullableElements => false;

	[CanBeNull]
	private Type ElementType {
		get {
			Type type = ElementTypeExpression.GetReferencedType();
			if (type == null || !NullableElements || !type.IsValueType || Nullable.GetUnderlyingType(type) != null) return type;
			return typeof(Nullable<>).MakeGenericType(type);
		}
	}

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);
		Lengths = args.Skip(2).Cast<Expression>().ToArray();
	}

	public override Type GetExpectedType() {
		Type elementType = ElementType;
		if (elementType == null) return null;
		return Lengths.Length == 1 ? elementType.MakeArrayType() : elementType.MakeArrayType(Lengths.Length);
	}

	public override void Validate(ParseResult parseResult) {
		CheckType(ElementTypeExpression, parseResult);
		foreach (Expression length in Lengths)
			if (CheckValue(length, parseResult) && !TypeRules.IsImplicitlyConvertible(length.GetExpectedType(), typeof(int)))
				length.AddError(parseResult, $"The length of an array has to be an int, not {TypeNames.GetDisplayName(length.GetExpectedType())}");
	}

	public override object Evaluate(DataContext context) {
		Type elementType = ElementType ?? throw new EvaluationException("Invalid array type", this);
		int[] lengths = Lengths.Select(l => (int)TypeRules.ConvertImplicitly(l.Evaluate(context), typeof(int))).ToArray();
		if (lengths.Any(l => l < 0)) throw new EvaluationException($"The length of an array can't be negative ({string.Join(", ", lengths)})", this);
		return Array.CreateInstance(elementType, lengths);
	}
}

// new T?[length], which NullableTypeExpression doesn't match, as T?[...] with a length could also be the start of a conditional (cond ? [1] : [2])
[ExpressionPattern(@"(?<!\S)new ", typeof(Expression), @" \? \[ ", typeof(Expression), @"(?: , ", typeof(Expression), @")* \]", AutoSpace = false)]
[PatternPriority(3)]
public class NullableArrayCreationExpression : ArrayCreationExpression {
	protected override bool NullableElements => true;
}

/**
 * [a, b, c], an array of the elements, like a collection expression in C#. As there is no type to convert it to, the element type is the type that all elements convert to
 * (e.g. float for [1, 2.5f]), so it can be used where an array, IList<T>, IReadOnlyList<T> or IEnumerable<T> is expected (but not a List<T>, use new List<T>([...]) for that).
 * A [ that follows a value is an element access instead (see IndexerExpression), which the negative lookbehind checks (in placeholders, values start with $).
 * The second pattern matches empty collections, which have no element type, and are reported.
 */
[ExpressionPattern(@"(?<!\$\S*(?:\x20W)*\x20)\[ ", typeof(Expression), @"(?: , ", typeof(Expression), @")* \]", AutoSpace = false)]
[ExpressionPattern(@"(?<!\$\S*(?:\x20W)*\x20)\[ \]", AutoSpace = false)]
[PatternPriority(2)]
public class CollectionExpression : Expression {
	[CanBeNull] private Type elementType;

	public Expression[] Elements => args.Cast<Expression>().ToArray();

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);
		Type[] types = Elements.Select(e => DescribeNonValue(e) == null ? e.GetExpectedType() : null).ToArray();
		if (types.Length == 0 || types.Any(t => t == null)) return;

		// the first type that all elements convert to, null elements only need a type that can be null
		elementType = types.Where(t => t != TypeRules.NULL).Distinct().FirstOrDefault(candidate => types.All(t => TypeRules.IsImplicitlyConvertible(t, candidate)));
	}

	public override Type GetExpectedType() {
		return elementType?.MakeArrayType();
	}

	public override void Validate(ParseResult parseResult) {
		bool elementsValid = true;
		foreach (Expression element in Elements) elementsValid &= CheckValue(element, parseResult);

		if (Elements.Length == 0) AddError(parseResult, "An empty collection has no element type, use e.g. new int[0] instead");
		else if (elementsValid && elementType == null && Elements.All(e => e.GetExpectedType() == TypeRules.NULL)) AddError(parseResult, "The element type can't be determined, as all elements are null");
		else if (elementsValid && elementType == null) AddError(parseResult, $"The elements have no common type ({string.Join(", ", Elements.Select(e => TypeNames.GetDisplayName(e.GetExpectedType())).Distinct())}), cast one of them to the type of the collection");
	}

	public override object Evaluate(DataContext context) {
		if (elementType == null) throw new EvaluationException("Invalid collection", this);

		Array array = Array.CreateInstance(elementType, Elements.Length);
		for (int i = 0; i < Elements.Length; i++) array.SetValue(TypeRules.ConvertImplicitly(Elements[i].Evaluate(context), elementType), i);
		return array;
	}
}

/**
 * Element access like list[0], dictionary[key], array[i] or grid[x, y] (for multidimensional arrays), using the indexer of the type (the property C# compiles this[...] to).
 */
[ExpressionPattern(typeof(Expression), @" \[ ", typeof(Expression), @"(?: , ", typeof(Expression), @")* \]", AutoSpace = false)]
[PatternPriority(2)]
public class IndexerExpression : Expression {
	[CanBeNull] private MethodCall getter;
	[CanBeNull] private string resolveError;
	[CanBeNull] private Type elementType;

	public Expression Object => (Expression)args[0];
	public Expression[] Arguments { get; private set; }

	private bool IsArrayAccess => Object.GetExpectedType()?.IsArray == true;

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);
		Arguments = args.Skip(1).Cast<Expression>().ToArray();
		Type objectType = Object.GetExpectedType();
		if (objectType == null || objectType == TypeRules.NULL || DescribeNonValue(Object) != null) return;
		if (Arguments.Any(a => a.GetExpectedType() == null || DescribeNonValue(a) != null)) return;

		if (objectType.IsArray) {
			int rank = objectType.GetArrayRank();
			Expression nonInt = Arguments.FirstOrDefault(a => !TypeRules.IsImplicitlyConvertible(a.GetExpectedType(), typeof(int)));
			if (rank != Arguments.Length) resolveError = $"{TypeNames.GetDisplayName(objectType)} is indexed with {rank} {(rank == 1 ? "index" : "indices")}, not {Arguments.Length}";
			else if (nonInt != null) resolveError = $"Arrays are indexed with int, not {TypeNames.GetDisplayName(nonInt.GetExpectedType())}";
			else elementType = objectType.GetElementType();
			return;
		}

		getter = MemberResolver.ResolveMethod(objectType, "get_" + MemberResolver.GetIndexerName(objectType), false, ArgumentTypes, out resolveError);
		if (getter == null && !MemberResolver.HasMethod(objectType, "get_" + MemberResolver.GetIndexerName(objectType), false)) resolveError = $"{TypeNames.GetDisplayName(objectType)} can't be indexed";
		elementType = getter?.ReturnType;
	}

	private Type[] ArgumentTypes => Arguments.Select(a => a.GetExpectedType()).ToArray();

	public override Type GetExpectedType() {
		return elementType;
	}

	public override void Validate(ParseResult parseResult) {
		bool valid = CheckValue(Object, parseResult);
		foreach (Expression argument in Arguments) valid &= CheckValue(argument, parseResult);
		if (valid && Object.GetExpectedType() == TypeRules.NULL) AddError(parseResult, "null can't be indexed");
		else if (valid && resolveError != null) AddError(parseResult, resolveError);
	}

	public override object Evaluate(DataContext context) {
		object target = EvaluateTarget(context, out object[] indices);
		return Get(target, indices);
	}

	// evaluates the indexed object and the indices (once, so compound assignments get and set the same element)
	internal object EvaluateTarget(DataContext context, out object[] indices) {
		if (elementType == null) throw new EvaluationException(resolveError ?? "Invalid element access", this);
		object target = Object.Evaluate(context);
		if (target == null) throw new EvaluationException("Trying to index null", this);
		indices = Arguments.Select(a => a.Evaluate(context)).ToArray();
		return target;
	}

	internal object Get(object target, object[] indices) {
		if (!IsArrayAccess) return MemberResolver.Invoke(getter, target, indices, this);

		try {
			return ((Array)target).GetValue(ToArrayIndices(indices));
		}
		catch (IndexOutOfRangeException e) {
			throw OutOfRange((Array)target, indices, e);
		}
	}

	private static int[] ToArrayIndices(object[] indices) {
		return indices.Select(i => (int)TypeRules.ConvertImplicitly(i, typeof(int))).ToArray();
	}

	private EvaluationException OutOfRange(Array array, object[] indices, Exception exception) {
		string size = string.Join(" x ", Enumerable.Range(0, array.Rank).Select(array.GetLength));
		return new EvaluationException($"Index {string.Join(", ", indices)} is outside of the array (size {size})", this, exception);
	}

	// the setter for values of the given type, null (with the reason in error) if the element can't be set
	[CanBeNull]
	internal MethodCall ResolveSetter(Type valueType, out string error) {
		error = null;
		if (IsArrayAccess) return null;
		Type objectType = Object.GetExpectedType();
		MethodCall setter = MemberResolver.ResolveMethod(objectType, "set_" + MemberResolver.GetIndexerName(objectType), false, ArgumentTypes.Append(valueType).ToArray(), out error);
		if (setter == null && !MemberResolver.HasMethod(objectType, "set_" + MemberResolver.GetIndexerName(objectType), false)) error = $"The elements of {TypeNames.GetDisplayName(objectType)} are read only";
		return setter;
	}

	internal void Set(object target, object[] indices, object value, [CanBeNull] MethodCall setter) {
		if (!IsArrayAccess) {
			MemberResolver.Invoke(setter, target, indices.Append(value).ToArray(), this);
			return;
		}

		try {
			((Array)target).SetValue(TypeRules.ConvertImplicitly(value, elementType), ToArrayIndices(indices));
		}
		catch (IndexOutOfRangeException e) {
			throw OutOfRange((Array)target, indices, e);
		}
	}
}

/**
 * target = value, and the compound assignments (target += value etc.), which are only allowed in actions (see ParseTarget.Action), as evaluating everything else must not change anything.
 * The target has to be a field, property or indexer. Like in C#, the result is the assigned value.
 * The lowest priority after sequences, so everything else on both sides is resolved first. The negative lookahead makes chained assignments right associative (a = b = c is a = (b = c)),
 * which is why the patterns don't use AutoSpace: it would put a space between the value and the lookahead.
 */
public abstract class AssignmentExpression : Expression {
	internal const string NOT_FOLLOWED_BY_ASSIGNMENT = @"(?!(?:\x20W)*\x20(?:=|\+=|-=|\*=|/=)(?:\x20|$))";

	[CanBeNull] private Type targetType;
	[CanBeNull] private string targetError;
	[CanBeNull] private MethodCall indexerSetter;
	[CanBeNull] private Type operationResultType;
	[CanBeNull] private MethodCall userOperator;

	public Expression Target => (Expression)args[0];
	public Expression Value => (Expression)args[1];

	// the operator of compound assignments (e.g. '+' for +=), null for =
	protected abstract char? Operation { get; }

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);
		targetType = ResolveTarget(out targetError);

		Type valueType = Value.GetExpectedType();
		if (targetType == null || valueType == null || DescribeNonValue(Value) != null) return;
		if (Target is IndexerExpression indexer) indexerSetter = indexer.ResolveSetter(valueType == TypeRules.NULL ? targetType : valueType, out targetError);
		if (Operation != null) operationResultType = CalculationExpression.ResolveResultType(Operation.Value, targetType, CalculationExpression.GetConstantType(Value, targetType) ?? valueType, out userOperator);
	}

	// the type of what is assigned to, null if it can't be assigned (with the reason in error, unless it is an error of the target itself)
	[CanBeNull]
	private Type ResolveTarget([CanBeNull] out string error) {
		error = null;
		switch (Target) {
			case FieldAccessExpression fieldAccess:
				VariableExpression field = fieldAccess.Field;
				if (field.Kind != VariableKind.Field && field.Kind != VariableKind.Property) return null; // reported by the field access
				if (!MemberResolver.IsWritable(field.Member)) {
					error = $"'{field.VariableName}' is read only";
					return null;
				}

				// like in C#, as the value would only be changed in a copy (e.g. rectTransform.anchorMin.x = 1)
				if (!field.IsStatic && fieldAccess.Object.GetExpectedType()?.IsValueType == true) {
					error = $"Cannot modify '{field.VariableName}', as it is a member of a {TypeNames.GetDisplayName(fieldAccess.Object.GetExpectedType())} value, which would only change a copy. Assign a whole new value instead";
					return null;
				}

				return field.GetExpectedType();
			case IndexerExpression indexer:
				return indexer.GetExpectedType(); // the setter is resolved once the value type is known
			case VariableExpression { Kind: VariableKind.ContextVariable } variable:
				error = $"The variable '{variable.VariableName}' can't be assigned, only fields, properties and elements";
				return null;
			default:
				if (Target.GetExpectedType() != null && DescribeNonValue(Target) == null) error = "Only fields, properties and elements (e.g. list[0]) can be assigned";
				return null;
		}
	}

	public override Type GetExpectedType() {
		return targetType != null && targetError == null ? targetType : null;
	}

	public override void Validate(ParseResult parseResult) {
		bool valueValid = CheckValue(Value, parseResult);
		if (targetError != null) {
			Target.AddError(parseResult, targetError);
			return;
		}

		if (targetType == null || !valueValid) return;
		Type valueType = Value.GetExpectedType();
		if (Operation == null) {
			if (!TypeRules.IsImplicitlyConvertible(valueType, targetType)) AddError(parseResult, $"Cannot assign {TypeNames.GetDisplayName(valueType)} to {TypeNames.GetDisplayName(targetType)}");
			return;
		}

		if (operationResultType == null) AddError(parseResult, $"Operator '{Operation}=' cannot be applied to operands of type {TypeNames.GetDisplayName(targetType)} and {TypeNames.GetDisplayName(valueType)}");
		else if (!IsCompoundResultAssignable(valueType)) AddError(parseResult, $"Cannot assign the result ({TypeNames.GetDisplayName(operationResultType)}) to {TypeNames.GetDisplayName(targetType)}");
	}

	/**
	 * Like in C#, the result of a compound assignment is also converted explicitly if the value itself converts implicitly to the target,
	 * which includes integer literals for smaller integer types (so byteField += 1 works, but intField += 0.5 does not).
	 */
	private bool IsCompoundResultAssignable(Type valueType) {
		if (TypeRules.IsImplicitlyConvertible(operationResultType, targetType)) return true;
		if (userOperator != null || !TypeRules.IsExplicitlyConvertible(operationResultType, targetType)) return false;
		return TypeRules.IsImplicitlyConvertible(valueType, targetType) || (Value is NumberExpression && TypeRules.IsIntegral(valueType) && TypeRules.IsIntegral(targetType));
	}

	public override object Evaluate(DataContext context) {
		if (GetExpectedType() == null) throw new EvaluationException(targetError ?? "Invalid assignment", this);

		switch (Target) {
			case FieldAccessExpression fieldAccess: {
				VariableExpression field = fieldAccess.Field;
				object owner = null;
				if (!field.IsStatic) {
					owner = fieldAccess.Object.Evaluate(context);
					if (owner == null) throw new EvaluationException($"Trying to set '{field.VariableName}' of null", this);
				}

				object value = ComputeValue(context, () => MemberResolver.GetValue(field.Member, owner, this));
				MemberResolver.SetValue(field.Member, owner, value, this);
				return value;
			}
			case IndexerExpression indexer: {
				object owner = indexer.EvaluateTarget(context, out object[] indices);
				object value = ComputeValue(context, () => indexer.Get(owner, indices));
				indexer.Set(owner, indices, value, indexerSetter);
				return value;
			}
			default:
				throw new EvaluationException("Only fields, properties and elements can be assigned", this);
		}
	}

	// the value to assign, converted to the type of the target. getCurrent is only called by compound assignments
	private object ComputeValue(DataContext context, Func<object> getCurrent) {
		if (Operation == null) return TypeRules.ConvertImplicitly(Value.Evaluate(context), targetType);

		object current = getCurrent();
		object result = CalculationExpression.PerformOperation(current, Value.Evaluate(context), Operation.Value, userOperator, operationResultType, this);
		return TypeRules.IsImplicitlyConvertible(operationResultType, targetType) ? TypeRules.ConvertImplicitly(result, targetType) : TypeRules.Cast(result, targetType, this);
	}
}

[ExpressionPattern(typeof(Expression), " = ", typeof(Expression), NOT_FOLLOWED_BY_ASSIGNMENT, AutoSpace = false)]
[PatternPriority(-1000)]
public class SimpleAssignmentExpression : AssignmentExpression {
	protected override char? Operation => null;
}

[ExpressionPattern(typeof(Expression), @" \+= ", typeof(Expression), NOT_FOLLOWED_BY_ASSIGNMENT, AutoSpace = false)]
[PatternPriority(-1000)]
public class AddAssignmentExpression : AssignmentExpression {
	protected override char? Operation => '+';
}

[ExpressionPattern(typeof(Expression), " -= ", typeof(Expression), NOT_FOLLOWED_BY_ASSIGNMENT, AutoSpace = false)]
[PatternPriority(-1000)]
public class SubtractAssignmentExpression : AssignmentExpression {
	protected override char? Operation => '-';
}

[ExpressionPattern(typeof(Expression), @" \*= ", typeof(Expression), NOT_FOLLOWED_BY_ASSIGNMENT, AutoSpace = false)]
[PatternPriority(-1000)]
public class MultiplyAssignmentExpression : AssignmentExpression {
	protected override char? Operation => '*';
}

[ExpressionPattern(typeof(Expression), " /= ", typeof(Expression), NOT_FOLLOWED_BY_ASSIGNMENT, AutoSpace = false)]
[PatternPriority(-1000)]
public class DivideAssignmentExpression : AssignmentExpression {
	protected override char? Operation => '/';
}

/**
 * Statements separated by ';', which are executed in order. Only actions can have multiple statements (see ParseTarget.Action), as nothing else should have side effects.
 * The second pattern allows a trailing ';' after the last statement.
 */
[ExpressionPattern(typeof(Expression), ";", typeof(Expression))]
[ExpressionPattern(typeof(Expression), @";(?=(?:\x20W)*$)")]
[PatternPriority(-2000)]
public class SequenceExpression : Expression {
	public Expression First => (Expression)args[0];
	[CanBeNull] public Expression Second => args.Length > 1 ? (Expression)args[1] : null;

	public override Type GetExpectedType() {
		return typeof(void);
	}

	public override object Evaluate(DataContext context) {
		First.Evaluate(context);
		Second?.Evaluate(context);
		return null;
	}
}
}
