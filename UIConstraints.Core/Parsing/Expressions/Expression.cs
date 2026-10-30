using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UIConstraints.Core.Data;
using JetBrains.Annotations;

namespace UIConstraints.Core.Parsing.Expressions {
public abstract class Expression : ParsedElement {
	protected object[] args;
	protected ParseContext parseContext;

	// the part of the source this expression was parsed from (start inclusive, end exclusive)
	public int SourceStart { get; internal set; }
	public int SourceEnd { get; internal set; }

	public virtual void Prepare(object[] args, ParseContext context) {
		this.args = args;
		parseContext = context;
	}

	public virtual bool HidesInspectedChildren() // if true, the inspector will show the information of this expression instead of the child at that position
	{
		return false;
	}

	/**
	 * The type of the value of this expression, known before it is evaluated (typeof(void) for calls of methods without return value).
	 * Null if this is not a value (but a type or namespace), or if the type could not be determined because of an error, which is then reported by Validate.
	 */
	[CanBeNull]
	public virtual Type GetExpectedType() {
		return null;
	}

	// if this expression names a type instead of a value (e.g. 'Mathf' in 'Mathf.PI' or 'List<int>'), returns that type
	[CanBeNull]
	public virtual Type GetReferencedType() {
		return null;
	}

	// if this expression names a namespace (e.g. 'System.Collections' in 'System.Collections.Generic.List<int>'), returns its full name
	[CanBeNull]
	public virtual string GetReferencedNamespace() {
		return null;
	}

	/**
	 * Reports the errors of this expression (but not those of its children, which are validated themselves).
	 * Called once the whole source is parsed, as an expression may still change until then (e.g. a name is only known to be a member once the expression accessing it is parsed).
	 * Children are validated before their parents.
	 */
	public virtual void Validate(ParseResult parseResult) { }

	public abstract object Evaluate(DataContext context);

	internal void AddError(ParseResult parseResult, string message, [CanBeNull] string unresolvedIdentifier = null) {
		parseResult.AddExceptionMessage(message, SourceStart, SourceEnd, unresolvedIdentifier);
	}

	/**
	 * Checks that an operand can be used as a value, reports an error if it can't (e.g. because it is a type).
	 * Returns false without reporting anything if the operand has an error itself, as that was already reported when it was validated.
	 */
	internal static bool CheckValue(Expression operand, ParseResult parseResult) {
		string problem = DescribeNonValue(operand);
		if (problem != null) {
			operand.AddError(parseResult, problem);
			return false;
		}

		return operand.GetExpectedType() != null;
	}

	// like CheckValue, but for operands that have to be types
	internal static bool CheckType(Expression operand, ParseResult parseResult) {
		if (operand.GetReferencedType() != null) return true;

		string referencedNamespace = operand.GetReferencedNamespace();
		Type valueType = operand.GetExpectedType();
		if (referencedNamespace != null) operand.AddError(parseResult, $"'{referencedNamespace}' is a namespace, but is used like a type");
		else if (valueType != null) operand.AddError(parseResult, $"'{parseResult.source.Substring(operand.SourceStart, operand.SourceEnd - operand.SourceStart).Trim()}' is not a type");
		return false;
	}

	// whether the value of the expression is a nullable value type (e.g. int?)
	internal static bool IsNullable(Expression expression) {
		Type type = expression.GetExpectedType();
		return type != null && Nullable.GetUnderlyingType(type) != null;
	}

	// what is wrong with using this expression as a value, null if nothing is
	[CanBeNull]
	internal static string DescribeNonValue(Expression expression) {
		Type referencedType = expression.GetReferencedType();
		if (referencedType != null) return $"'{TypeNames.GetDisplayName(referencedType)}' is a type, which is not valid in the given context";
		string referencedNamespace = expression.GetReferencedNamespace();
		if (referencedNamespace != null) return $"'{referencedNamespace}' is a namespace, which is not valid in the given context";
		if (expression.GetExpectedType() == typeof(void)) return "A method without return value (void) does not have a value";
		return null;
	}
}

// binary operators, operands are matched by the pattern of the subclass
public abstract class BinaryExpression : Expression {
	public Expression Left => (Expression)args[0];
	public Expression Right => (Expression)args[1];
}

// identifiers and member accesses, which can name a value, a type or a namespace, depending on what they resolve to
public abstract class NameExpression : Expression {
	public abstract string Name { get; }

	// re-resolves this name as a generic type with the given amount of type arguments (e.g. 'List' in 'List<int>', which on its own would not be found)
	public abstract void ResolveAsGenericType(int arity);
}

public enum VariableKind {
	ContextVariable, // a variable declared in the ParseContext, its value comes from the DataContext at runtime (e.g. set by a ForEachConstraint)
	Type,
	Namespace,
	Field,
	Property,
	Method,
	Unresolved, // an identifier on its own that is neither a variable, type nor namespace
	Invalid // a member that does not exist in its type or namespace, or a member of something invalid
}

[ExpressionPattern(typeof(IdentifierToken))]
[PatternPriority(999)]
public class VariableExpression : NameExpression {
	private enum OwnerKind {
		None, // on its own, not a member of anything
		Value,
		Type, // a static member or nested type
		Namespace,
		Invalid // something with an error, or a value without members (void)
	}

	private OwnerKind ownerKind;
	[CanBeNull] private Type ownerType;
	[CanBeNull] private string ownerNamespace;
	private int genericArity;
	private bool isMethodName;

	// resolved lazily, as most identifiers turn out to be member names, which would be wasted effort to resolve on their own (and may require an expensive namespace lookup)
	private bool resolved;
	private VariableKind kind;
	private bool isStatic;
	[CanBeNull] private MemberInfo member;
	[CanBeNull] private Type valueType;
	[CanBeNull] private Type referencedType;
	[CanBeNull] private string referencedNamespace;

	public string VariableName => ((IdentifierToken)args[0]).Identifier;
	public override string Name => VariableName;

	// what this identifier refers to. On its own a variable is a DataContext lookup, a type or a namespace, wrapping expressions such as FieldAccessExpression re-resolve it as a member
	public VariableKind Kind {
		get {
			EnsureResolved();
			return kind;
		}
	}

	// whether this is a static member (only meaningful if resolved as a member)
	public bool IsStatic {
		get {
			EnsureResolved();
			return isStatic;
		}
	}

	// the field or property this variable refers to, if it was resolved as one
	[CanBeNull]
	public MemberInfo Member {
		get {
			EnsureResolved();
			return member;
		}
	}

	// whether this is a member of something that has an error itself, in which case the member is not reported as an error too
	public bool HasInvalidOwner => ownerKind == OwnerKind.Invalid;

	// marks this as a member of the given expression (a value, type or namespace)
	public void SetOwner(Expression owner) {
		ownerNamespace = owner.GetReferencedNamespace();
		ownerType = owner.GetReferencedType();
		if (ownerNamespace != null) {
			ownerKind = OwnerKind.Namespace;
		}
		else if (ownerType != null) {
			ownerKind = OwnerKind.Type;
		}
		else {
			ownerType = owner.GetExpectedType();
			ownerKind = ownerType != null && ownerType != typeof(void) && ownerType != TypeRules.NULL ? OwnerKind.Value : OwnerKind.Invalid;
		}

		resolved = false;
	}

	// marks this as the name of a called method (only meaningful for members)
	public void SetMethodName() {
		isMethodName = true;
		resolved = false;
	}

	public override void ResolveAsGenericType(int arity) {
		genericArity = arity;
		resolved = false;
	}

	private string MetadataName => genericArity > 0 ? VariableName + "`" + genericArity : VariableName;

	private void EnsureResolved() {
		if (resolved) return;
		resolved = true;

		kind = VariableKind.Invalid;
		isStatic = false;
		member = null;
		valueType = null;
		referencedType = null;
		referencedNamespace = null;
		switch (ownerKind) {
			case OwnerKind.None:
				ResolveOnItsOwn();
				break;
			case OwnerKind.Namespace:
				if ((referencedType = TypeResolver.FindType(ownerNamespace + "." + MetadataName)) != null) {
					kind = VariableKind.Type;
				}
				else if (genericArity == 0 && TypeResolver.NamespaceExists(ownerNamespace + "." + VariableName)) {
					kind = VariableKind.Namespace;
					referencedNamespace = ownerNamespace + "." + VariableName;
				}

				break;
			case OwnerKind.Type:
			case OwnerKind.Value:
				ResolveAsMember(ownerType, ownerKind == OwnerKind.Type);
				break;
		}
	}

	private void ResolveOnItsOwn() {
		// like in C#, variables hide types with the same name
		if (genericArity == 0 && parseContext.variables.TryGetValue(VariableName, out Type variableType)) {
			kind = VariableKind.ContextVariable;
			valueType = variableType;
		}
		else if ((referencedType = parseContext.ResolveType(MetadataName)) != null) {
			kind = VariableKind.Type;
		}
		else if (genericArity == 0 && TypeResolver.NamespaceExists(VariableName)) {
			kind = VariableKind.Namespace;
			referencedNamespace = VariableName;
		}
		else {
			kind = VariableKind.Unresolved;
		}
	}

	private void ResolveAsMember(Type type, bool isStaticAccess) {
		isStatic = isStaticAccess;
		if (isMethodName) {
			if (MemberResolver.HasMethod(type, VariableName, isStaticAccess, genericArity)) kind = VariableKind.Method;
			return;
		}

		if (genericArity == 0) {
			member = MemberResolver.FindFieldOrProperty(type, VariableName, isStaticAccess);
			switch (member) {
				case FieldInfo fieldInfo:
					kind = VariableKind.Field;
					valueType = fieldInfo.FieldType;
					return;
				case PropertyInfo propertyInfo:
					kind = VariableKind.Property;
					valueType = propertyInfo.PropertyType;
					return;
			}
		}

		if (isStaticAccess && (referencedType = MemberResolver.FindNestedType(type, MetadataName)) != null) kind = VariableKind.Type;
	}

	public override void Validate(ParseResult parseResult) {
		// members are validated by the expression accessing them, as it knows what they are a member of
		if (ownerKind != OwnerKind.None || Kind != VariableKind.Unresolved) return;

		string displayName = TypeNames.GetDisplayNameOfMetadataName(MetadataName);
		if (parseContext.aliases.TryGetValue(VariableName, out string aliasTarget)) {
			AddError(parseResult, $"The alias '{VariableName}' refers to '{aliasTarget}', which is not a valid type (aliases need fully qualified names, like 'System.Collections.Generic.List<int>')");
			return;
		}

		List<Type> candidates = TypeResolver.FindCandidates(MetadataName, parseContext.namespaces);
		string message;
		if (candidates.Count > 1) message = $"'{displayName}' is ambiguous between {string.Join(" and ", candidates.Select(TypeNames.GetFullName))}";
		else if (genericArity > 0) message = $"The type '{displayName}' could not be found";
		else message = $"Unknown identifier '{VariableName}'";
		AddError(parseResult, message, MetadataName);
	}

	// reports that this member does not exist (or can't be used the way it is), with a message fitting what it is a member of
	internal void AddErrorForMissingMember(ParseResult parseResult) {
		string name = TypeNames.GetDisplayNameOfMetadataName(MetadataName);
		string message;
		if (ownerKind == OwnerKind.Namespace) {
			message = isMethodName ? $"Methods can't be called on the namespace '{ownerNamespace}'" : $"The type or namespace '{name}' does not exist in the namespace '{ownerNamespace}'";
		}
		else if (ownerKind == OwnerKind.Type || ownerKind == OwnerKind.Value) {
			bool staticAccess = ownerKind == OwnerKind.Type;
			string typeName = TypeNames.GetDisplayName(ownerType);
			if (isMethodName) {
				if (!MemberResolver.HasMethod(ownerType, VariableName, !staticAccess, genericArity))
					message = genericArity > 0 ? $"'{typeName}' does not contain a generic method named '{VariableName}' with {genericArity} type argument{(genericArity == 1 ? "" : "s")}" : $"'{typeName}' does not contain a method named '{VariableName}'";
				else if (staticAccess) message = $"'{VariableName}' is not static, it can only be called on an instance of {typeName}";
				else message = $"'{VariableName}' is static, call it on the type '{typeName}' instead";
			}
			else if (MemberResolver.HasMethod(ownerType, VariableName, staticAccess)) {
				message = $"'{VariableName}' is a method, which needs parentheses to be called";
			}
			else if (genericArity == 0 && MemberResolver.FindFieldOrProperty(ownerType, VariableName, !staticAccess) != null) {
				message = staticAccess ? $"'{VariableName}' is not static, it can only be accessed on an instance of {typeName}" : $"'{VariableName}' is static, access it through the type '{typeName}' instead";
			}
			else {
				message = staticAccess ? $"'{typeName}' does not contain a static member or nested type named '{name}'" : $"'{typeName}' does not contain a member named '{name}'";
			}
		}
		else {
			return; // not a member, or a member of something invalid (reported elsewhere)
		}

		AddError(parseResult, message);
	}

	public override object Evaluate(DataContext context) {
		if (Kind != VariableKind.ContextVariable) throw new EvaluationException($"'{VariableName}' is not a value", this);
		if (!context.TryGet(VariableName, out object value)) throw new EvaluationException($"The variable '{VariableName}' is not available here", this);

		bool fitsType = value == null ? TypeRules.CanBeNull(valueType) : valueType.IsInstanceOfType(value);
		if (!fitsType) throw new EvaluationException($"The variable '{VariableName}' is declared as {TypeNames.GetDisplayName(valueType)}, but its value is {(value == null ? "null" : TypeNames.GetDisplayName(value.GetType()))}", this);
		return value;
	}

	public override Type GetExpectedType() {
		EnsureResolved();
		return valueType;
	}

	public override Type GetReferencedType() {
		EnsureResolved();
		return kind == VariableKind.Type ? referencedType : null;
	}

	public override string GetReferencedNamespace() {
		EnsureResolved();
		return kind == VariableKind.Namespace ? referencedNamespace : null;
	}
}

// Parentheses directly following a name are the parentheses of a function call (which FunctionCallExpression handles), the negative lookbehind prevents matching those.
// The content may also be a type, which is how casts start (see CastExpression).
[ExpressionPattern(@"(?<!\$\S*(?:NameExpression>\S+|IdentifierToken) )\(", typeof(Expression), @"\)")]
[PatternPriority(999)]
public class BracketExpression : Expression {
	public Expression Content => (Expression)args[0];

	public override object Evaluate(DataContext context) {
		return Content.Evaluate(context);
	}

	public override Type GetExpectedType() {
		return Content.GetExpectedType();
	}

	public override Type GetReferencedType() {
		return Content.GetReferencedType();
	}
}

[ExpressionPattern(typeof(StringToken))]
[PatternPriority(999)]
public class StringExpression : Expression {
	public override object Evaluate(DataContext context) {
		return ((StringToken)args[0]).Value;
	}

	public override Type GetExpectedType() {
		return typeof(string);
	}
}

[ExpressionPattern(typeof(NumberToken))]
[PatternPriority(999)]
public class NumberExpression : Expression {
	public object Value => ((NumberToken)args[0]).Value;

	public override object Evaluate(DataContext context) {
		return Value;
	}

	public override Type GetExpectedType() {
		return Value.GetType();
	}
}

// keywords are their own placeholders, the lookarounds make sure we match the whole placeholder and not just a part of another one
[ExpressionPattern(@"(?<!\S)(?:true|false)(?!\S)")]
[PatternPriority(999)]
public class BooleanExpression : Expression {
	public bool Value => ((IdentifierToken)args[0]).Identifier == "true";

	public override object Evaluate(DataContext context) {
		return Value;
	}

	public override Type GetExpectedType() {
		return typeof(bool);
	}
}

[ExpressionPattern(@"(?<!\S)null(?!\S)")]
[PatternPriority(999)]
public class NullExpression : Expression {
	public override object Evaluate(DataContext context) {
		return null;
	}

	public override Type GetExpectedType() {
		return TypeRules.NULL;
	}
}

[ExpressionPattern(typeof(Expression), @"\?", typeof(Expression), @":", typeof(Expression))]
[PatternPriority(-999)]
public class ConditionalExpression : Expression {
	[CanBeNull] private Type resultType;

	public Expression Condition => (Expression)args[0];
	public Expression FirstExpression => (Expression)args[1];
	public Expression SecondExpression => (Expression)args[2];

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);
		Type firstType = FirstExpression.GetExpectedType();
		Type secondType = SecondExpression.GetExpectedType();
		if (firstType != null && secondType != null && DescribeNonValue(FirstExpression) == null && DescribeNonValue(SecondExpression) == null) resultType = TypeRules.GetCommonType(firstType, secondType);
	}

	public override void Validate(ParseResult parseResult) {
		if (CheckValue(Condition, parseResult) && Condition.GetExpectedType() != typeof(bool)) Condition.AddError(parseResult, $"The condition has to be a bool, not {TypeNames.GetDisplayName(Condition.GetExpectedType())}");

		bool firstValid = CheckValue(FirstExpression, parseResult);
		bool secondValid = CheckValue(SecondExpression, parseResult);
		if (firstValid && secondValid && resultType == null)
			AddError(parseResult, $"The type of the conditional expression can't be determined, as there is no implicit conversion between {TypeNames.GetDisplayName(FirstExpression.GetExpectedType())} and {TypeNames.GetDisplayName(SecondExpression.GetExpectedType())}");
	}

	public override object Evaluate(DataContext context) {
		object conditionValue = Condition.Evaluate(context);
		if (conditionValue is not bool condition) throw new EvaluationException("The condition has to be a bool", this);
		object result = condition ? FirstExpression.Evaluate(context) : SecondExpression.Evaluate(context);
		return TypeRules.ConvertImplicitly(result, resultType);
	}

	public override Type GetExpectedType() {
		return resultType;
	}
}

// As code gets parsed left to right when priorities are equal, this resolves
// A.B.C
// as
// FieldAccessExpression(FieldAccessExpression(VariableExpression(A), VariableExpression(B)), VariableExpression(C))
// Depending on the object, this accesses an instance member, a static member or nested type of a type, or a type or namespace in a namespace.
[ExpressionPattern(typeof(Expression), @"\.", typeof(VariableExpression))]
[PatternPriority(2)]
public class FieldAccessExpression : NameExpression {
	private bool isMethodGroup;

	public Expression Object => (Expression)args[0];
	public VariableExpression Field => (VariableExpression)args[1];
	public override string Name => Field.VariableName;

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);
		Field.SetOwner(Object);
	}

	// marks this as the method called by a FunctionCallExpression, which does the validation as it knows the arguments
	public void MarkAsMethodGroup() {
		isMethodGroup = true;
		Field.SetMethodName();
	}

	public override void ResolveAsGenericType(int arity) {
		Field.ResolveAsGenericType(arity);
	}

	public override Type GetExpectedType() {
		return isMethodGroup ? null : Field.GetExpectedType();
	}

	public override Type GetReferencedType() {
		return Field.GetReferencedType();
	}

	public override string GetReferencedNamespace() {
		return Field.GetReferencedNamespace();
	}

	public override void Validate(ParseResult parseResult) {
		if (isMethodGroup) return;
		if (Field.HasInvalidOwner) {
			CheckValue(Object, parseResult); // reports void, other errors were reported for the object itself
			return;
		}

		if (Field.Kind == VariableKind.Invalid) Field.AddErrorForMissingMember(parseResult);
	}

	public override object Evaluate(DataContext context) {
		if (Field.Kind != VariableKind.Field && Field.Kind != VariableKind.Property) throw new EvaluationException($"'{Field.VariableName}' is not a value", this);

		object target = null;
		if (!Field.IsStatic) {
			target = Object.Evaluate(context);
			if (target == null && !IsNullable(Object)) throw new EvaluationException($"Trying to access '{Field.VariableName}' of null", this); // nullable values have members for null (e.g. HasValue)
		}

		return MemberResolver.GetValue(Field.Member, target, this);
	}
}

/**
 * Methods are always called on something (e.g. player.GetHealth() or Mathf.Max(a, b)), as there is no implicit 'this' to call them on otherwise.
 * Generic methods are called with type arguments (the second pattern, e.g. gameObject.GetComponent<Image>(), where the name with the type arguments is a GenericTypeExpression),
 * or without them if they can be inferred from the arguments (e.g. Enumerable.Max(numbers)).
 */
[ExpressionPattern(typeof(FieldAccessExpression), @" \((?: ", typeof(Expression), @"(?: , ", typeof(Expression), @")*)? \)", AutoSpace = false)]
[ExpressionPattern(typeof(GenericTypeExpression), @" \((?: ", typeof(Expression), @"(?: , ", typeof(Expression), @")*)? \)", AutoSpace = false)]
[PatternPriority(2)]
public class FunctionCallExpression : Expression {
	[CanBeNull] private MethodCall resolvedCall;
	[CanBeNull] private string resolveError;
	[CanBeNull] private GenericTypeExpression genericMethod; // the name with the type arguments, for calls with explicit type arguments

	// null if a generic method is not called on anything (e.g. Max<int>(a, b)), which Validate reports
	[CanBeNull] public FieldAccessExpression Target { get; private set; }
	public string MethodName => Target?.Field.VariableName ?? genericMethod.GenericName.Name;
	public Expression[] Arguments { get; private set; }

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);
		Arguments = args.Skip(1).Cast<Expression>().ToArray();
		genericMethod = args[0] as GenericTypeExpression;
		genericMethod?.MarkAsMethodName();
		Target = genericMethod != null ? genericMethod.GenericName as FieldAccessExpression : (FieldAccessExpression)args[0];
		if (Target == null) return; // reported by Validate
		Target.MarkAsMethodGroup();
		if (Target.Field.Kind != VariableKind.Method) return; // reported by Validate

		// the overload can only be determined if all arguments and type arguments are valid
		if (Arguments.Any(a => a.GetExpectedType() == null || DescribeNonValue(a) != null)) return;
		Type[] typeArguments = genericMethod?.TypeArguments.Select(t => t.GetReferencedType()).ToArray();
		if (typeArguments != null && typeArguments.Any(t => t == null)) return;
		Type staticType = Target.Object.GetReferencedType();
		Type ownerType = staticType ?? Target.Object.GetExpectedType();
		resolvedCall = MemberResolver.ResolveMethod(ownerType, MethodName, staticType != null, Arguments.Select(a => a.GetExpectedType()).ToArray(), out resolveError, typeArguments);
	}

	public override Type GetExpectedType() {
		return resolvedCall?.ReturnType;
	}

	public override void Validate(ParseResult parseResult) {
		bool argumentsValid = true;
		foreach (Expression argument in Arguments) argumentsValid &= CheckValue(argument, parseResult);

		if (Target == null) {
			AddError(parseResult, $"Methods have to be called on something, e.g. Type.{MethodName}<...>() or value.{MethodName}<...>()");
			return;
		}

		if (Target.Field.HasInvalidOwner) {
			CheckValue(Target.Object, parseResult); // reports void, other errors were reported for the object itself
			return;
		}

		if (Target.Field.Kind != VariableKind.Method) {
			Target.Field.AddErrorForMissingMember(parseResult);
			return;
		}

		if (argumentsValid && resolveError != null) AddError(parseResult, resolveError);
	}

	public override object Evaluate(DataContext context) {
		if (resolvedCall == null) throw new EvaluationException($"No fitting overload of '{MethodName}' was found", this);

		object target = null;
		if (!resolvedCall.method.IsStatic) {
			target = Target.Object.Evaluate(context);
			if (target == null && !IsNullable(Target.Object)) throw new EvaluationException($"Trying to call '{MethodName}' on null", this); // nullable values have methods for null (e.g. GetValueOrDefault())
		}

		object[] values = new object[Arguments.Length];
		for (int i = 0; i < Arguments.Length; i++) values[i] = Arguments[i].Evaluate(context);
		return MemberResolver.Invoke(resolvedCall, target, values, this);
	}
}
}
