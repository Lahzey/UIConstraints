using System;
using System.Linq;
using System.Reflection;
using UIConstraints.Core.Data;
using JetBrains.Annotations;

namespace UIConstraints.Core.Parsing.Expressions {
public abstract class Expression : ParsedElement {
	protected object[] args;

	public virtual void Prepare(object[] args, ParseContext context) {
		this.args = args;
	}

	public virtual bool HidesInspectedChildren() // if true, the inspector will show the information of this expression instead of the child at that position
	{
		return false;
	}

	// the type that can be expected from this expression before it is executed, null if unknown
	[CanBeNull]
	public virtual Type GetExpectedType() {
		return null;
	}

	// if this expression names a type instead of a value (e.g. 'Mathf' in 'Mathf.PI'), returns that type, so wrapping expressions can access its static members
	[CanBeNull]
	public virtual Type GetReferencedType() {
		return null;
	}

	public abstract object Evaluate(DataContext context);
}

public enum VariableKind {
	Unknown, // a member of something whose type is not known before evaluation
	ContextVariable, // a name that is not known while parsing, but may be set in the DataContext at runtime (e.g. by a ForEachConstraint)
	DataKey, // a key registered in a DataKeyRegistry
	Type,
	Field,
	Property,
	Method,
	Invalid // a member that does not exist on a known type
}

[ExpressionPattern(typeof(IdentifierToken))]
[PatternPriority(999)]
public class VariableExpression : Expression {
	[CanBeNull] private Type expectedType;
	[CanBeNull] private Type referencedType;

	public string VariableName => ((IdentifierToken)args[0]).Identifier;

	// what this identifier refers to. On its own a variable is a DataContext lookup or a type name, wrapping expressions such as FieldAccessExpression re-resolve it as a member
	public VariableKind Kind { get; private set; }

	// the field or property this variable refers to, if it was resolved as a member and found
	[CanBeNull] public MemberInfo Member { get; private set; }

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);

		DataKeyBase dataKey = DataKeyBase.Get(VariableName);
		if (dataKey != null) {
			Kind = VariableKind.DataKey;
			expectedType = dataKey.type;
		}
		else if ((referencedType = context.ResolveType(VariableName)) != null) {
			Kind = VariableKind.Type;
		}
		else {
			Kind = VariableKind.ContextVariable;
		}
	}

	// marks this variable as a field, property or nested type of the given type (or of an instance of that type if not static)
	public void ResolveAsMember([CanBeNull] Type ownerType, bool isStatic) {
		expectedType = null;
		referencedType = null;
		Member = null;

		// object means unknown, as the actual value may be of any type
		if (ownerType == null || ownerType == typeof(object)) {
			Kind = VariableKind.Unknown;
			return;
		}

		Member = MemberResolver.FindFieldOrProperty(ownerType, VariableName, isStatic);
		switch (Member) {
			case FieldInfo fieldInfo:
				Kind = VariableKind.Field;
				expectedType = fieldInfo.FieldType;
				return;
			case PropertyInfo propertyInfo:
				Kind = VariableKind.Property;
				expectedType = propertyInfo.PropertyType;
				return;
		}

		if (isStatic && (referencedType = MemberResolver.FindNestedType(ownerType, VariableName)) != null) {
			Kind = VariableKind.Type;
			return;
		}

		Kind = VariableKind.Invalid;
	}

	// marks this variable as the name of a method of the given type (or of an instance of that type if not static)
	public void ResolveAsMethod([CanBeNull] Type ownerType, bool isStatic) {
		expectedType = null;
		referencedType = null;
		Member = null;

		if (ownerType == null || ownerType == typeof(object)) Kind = VariableKind.Unknown;
		else Kind = MemberResolver.HasMethod(ownerType, VariableName, isStatic) ? VariableKind.Method : VariableKind.Invalid;
	}

	public override object Evaluate(DataContext context) {
		if (Kind == VariableKind.Type) throw new EvaluationException($"'{VariableName}' is a type and cannot be used as a value", this);
		return context.Get(VariableName);
	}

	public override Type GetExpectedType() {
		return expectedType;
	}

	public override Type GetReferencedType() {
		return Kind == VariableKind.Type ? referencedType : null;
	}
}

// the negative lookbehind prevents matching the parentheses of a function call (parentheses directly following an expression), FunctionCallExpression handles those
[ExpressionPattern(@"(?<!\$\S+ )\(", typeof(Expression), @"\)")]
[PatternPriority(999)]
public class BracketExpression : Expression {
	public override object Evaluate(DataContext context) {
		return ((Expression)args[0]).Evaluate(context);
	}

	public override Type GetExpectedType() {
		return ((Expression)args[0]).GetExpectedType();
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
	public override object Evaluate(DataContext context) {
		return ((NumberToken)args[0]).Value;
	}

	public override Type GetExpectedType() {
		return ((NumberToken)args[0]).Value.GetType();
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
}

[ExpressionPattern(typeof(Expression), @"\?", typeof(Expression), @":", typeof(Expression))]
[PatternPriority(-999)]
public class ConditionalExpression : Expression {
	public Expression Condition => (Expression)args[0];
	public Expression FirstExpression => (Expression)args[1];
	public Expression SecondExpression => (Expression)args[2];

	public override object Evaluate(DataContext context) {
		object conditionValue = Condition.Evaluate(context);
		if (conditionValue == null) return FirstExpression.Evaluate(context);
		if (conditionValue is bool b) return b ? FirstExpression.Evaluate(context) : SecondExpression.Evaluate(context);
		throw new EvaluationException("ConditionalExpression condition must be a boolean", this);
	}

	public override Type GetExpectedType() {
		Type firstType = FirstExpression.GetExpectedType();
		return firstType == SecondExpression.GetExpectedType() ? firstType : null;
	}
}

// As code gets parsed left to right when priorities are equal, this resolves
// A.B.C
// as
// FieldAccessExpression(FieldAccessExpression(VariableExpression(A), VariableExpression(B)), VariableExpression(C))
// If the object is a type (e.g. Mathf.PI), static members are accessed. This includes nested types, which can be used to access their static members in turn.
[ExpressionPattern(typeof(Expression), @"\.", typeof(VariableExpression))]
[PatternPriority(1)]
public class FieldAccessExpression : Expression {
	public Expression Object => (Expression)args[0];
	public VariableExpression Field => (VariableExpression)args[1];

	// the type whose static members are accessed, null if the object is a value
	[CanBeNull] public Type StaticType { get; private set; }

	// the type the member is looked up in while parsing, null if not known before evaluation
	[CanBeNull] public Type OwnerType => StaticType ?? Object.GetExpectedType();

	// used when the member could not be found while parsing (e.g. because the type of the object was not known), so we do not have to look it up on every evaluation
	[CanBeNull] private Type runtimeType;
	[CanBeNull] private MemberInfo runtimeMember;

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);
		StaticType = Object.GetReferencedType();
		Field.ResolveAsMember(OwnerType, StaticType != null);
	}

	public override Type GetExpectedType() {
		return Field.GetExpectedType();
	}

	public override Type GetReferencedType() {
		return Field.GetReferencedType();
	}

	public override object Evaluate(DataContext context) {
		string fieldName = Field.VariableName;
		if (Field.Kind == VariableKind.Type) throw new EvaluationException($"'{fieldName}' is a type and cannot be used as a value", this);

		if (StaticType != null) {
			if (Field.Member == null) throw new EvaluationException($"Static field or property '{fieldName}' not found for type '{StaticType.FullName}'", this);
			return MemberResolver.GetValue(Field.Member, null, this);
		}

		object objectValue = Object.Evaluate(context);
		if (objectValue == null) throw new EvaluationException($"Trying to access '{fieldName}' of null", this);

		MemberInfo memberInfo = Field.Member;
		if (memberInfo == null || !memberInfo.DeclaringType.IsInstanceOfType(objectValue)) memberInfo = GetRuntimeMember(objectValue.GetType());
		if (memberInfo == null) throw new EvaluationException($"Field or property '{fieldName}' not found for type '{objectValue.GetType().FullName}'", this);

		return MemberResolver.GetValue(memberInfo, objectValue, this);
	}

	private MemberInfo GetRuntimeMember(Type type) {
		if (type != runtimeType) {
			runtimeType = type;
			runtimeMember = MemberResolver.FindFieldOrProperty(type, Field.VariableName, false);
		}

		return runtimeMember;
	}
}

// Methods are always called on something (e.g. player.GetHealth() or Mathf.Max(a, b)), as there is no implicit 'this' to call them on otherwise
[ExpressionPattern(typeof(FieldAccessExpression), @" \((?: ", typeof(Expression), @"(?: , ", typeof(Expression), @")*)? \)", AutoSpace = false)]
[PatternPriority(1)]
public class FunctionCallExpression : Expression {
	public FieldAccessExpression Target => (FieldAccessExpression)args[0];
	public string MethodName => Target.Field.VariableName;
	public Expression[] Arguments { get; private set; }

	// the overload that was determined from the expected types while parsing, null if that was not possible
	[CanBeNull] private MethodInfo resolvedMethod;

	// used when the overload could not be determined while parsing, so we do not have to look it up on every evaluation
	[CanBeNull] private Type runtimeType;
	[CanBeNull] private Type[] runtimeArgTypes;
	[CanBeNull] private MethodInfo runtimeMethod;

	public override void Prepare(object[] args, ParseContext context) {
		base.Prepare(args, context);
		Arguments = args.Skip(1).Cast<Expression>().ToArray();

		Type ownerType = Target.OwnerType;
		bool isStatic = Target.StaticType != null;
		Target.Field.ResolveAsMethod(ownerType, isStatic);

		// only resolve the overload in advance if all argument types are known, otherwise the actual argument values may not fit
		Type[] argTypes = Arguments.Select(a => a.GetExpectedType()).ToArray();
		if (ownerType != null && ownerType != typeof(object) && argTypes.All(t => t != null))
			resolvedMethod = MemberResolver.ResolveMethod(ownerType, MethodName, isStatic, argTypes);
	}

	public override Type GetExpectedType() {
		return resolvedMethod == null || resolvedMethod.ReturnType == typeof(void) ? null : resolvedMethod.ReturnType;
	}

	public override object Evaluate(DataContext context) {
		Type staticType = Target.StaticType;
		object target = null;
		if (staticType == null) {
			target = Target.Object.Evaluate(context);
			if (target == null) throw new EvaluationException($"Trying to call '{MethodName}' on null", this);
		}

		object[] values = new object[Arguments.Length];
		for (int i = 0; i < Arguments.Length; i++) values[i] = Arguments[i].Evaluate(context);

		MethodInfo method = resolvedMethod;
		if (method == null || (target != null && !method.DeclaringType.IsInstanceOfType(target))) method = GetRuntimeMethod(staticType ?? target.GetType(), staticType != null, values);
		if (method == null) throw new EvaluationException($"No method '{MethodName}' with fitting parameters found for type '{(staticType ?? target.GetType()).FullName}'", this);

		return MemberResolver.Invoke(method, target, values, this);
	}

	private MethodInfo GetRuntimeMethod(Type type, bool isStatic, object[] values) {
		Type[] argTypes = values.Select(v => v?.GetType()).ToArray();
		if (type != runtimeType || runtimeArgTypes == null || !argTypes.SequenceEqual(runtimeArgTypes)) {
			runtimeType = type;
			runtimeArgTypes = argTypes;
			runtimeMethod = MemberResolver.ResolveMethodForValues(type, MethodName, isStatic, values);
		}

		return runtimeMethod;
	}
}

// probably should not support field assignment, but leaving this here if needed in the future
// [ValuePattern(typeof(FieldAccessExpression), @"=", typeof(Expression)), PatternPriority(-2)]
// public class FieldAssignmentValue : Expression
// {
//     public override object Evaluate(DataContext context)
//     {
//         object ObjectValue = ((FieldAccessExpression)args[0]).Object.Evaluate(context);
//         string FieldName = ((FieldAccessExpression)args[0]).FieldName;
//         object Expression = ((Expression)args[1]).Evaluate(context);
//         ObjectValue.GetType().GetField(FieldName).SetValue(ObjectValue, Expression);
//         return Expression;
//     }
//
//     public Expression Expression => (Expression) args[1];
// }
}
