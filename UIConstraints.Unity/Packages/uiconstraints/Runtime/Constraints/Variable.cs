using System;
using System.Collections.Generic;
using System.Reflection;
using JetBrains.Annotations;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;
using UIConstraints.Unity.Animation;
using UnityEngine;

namespace UIConstraints.Unity.Constraints {
/**
 * Sets a variable to the value of an expression, for the components after it on the same game object and all children.
 * E.g. with 'player' set to 'Player.Instance', children can just use 'player.name'. The value is evaluated once per render, no matter how many expressions use it.
 * The value can be animated, which animates it for everything that uses the variable (e.g. 'shownGold' for a counter and a bar showing the same value).
 */
[AddComponentMenu("UI Constraints/Variable")]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/variable.png")]
public class Variable : VariableConstraint, IAnimatedInputs {
	[SerializeField] private string variableName;
	[SerializeField] private CodeInput value;

	[CanBeNull] private Expression valueExpression;
	[CanBeNull] private AnimatedInput valueAnimation;

	public override string VariableName => variableName;

	protected override void Awake() {
		base.Awake();
		valueExpression = ParseField(nameof(value));
		Type type = valueExpression?.GetExpectedType();
		if (type != null && type != TypeRules.NULL) valueAnimation = AnimatedInput.Create(this, nameof(value), type);
	}

	// the value, as the type of the variable
	public Type GetAnimatedType(FieldInfo field) {
		if (field.Name != nameof(value)) return null;
		ParseResult parseResult = CodeInputs.Parse(this, field);
		Type type = parseResult.Success ? parseResult.Expression?.GetExpectedType() : null;
		return type == TypeRules.NULL ? null : type;
	}

	public override IEnumerable<KeyValuePair<string, Type>> GetDeclaredVariables(IReadOnlyDictionary<string, Type> availableVariables) {
		if (!IdentifierToken.IsValidIdentifier(variableName)) yield break;

		ParseResult parseResult = CodeInputs.Parse(this, CodeInputs.GetField(GetType(), nameof(value)), availableVariables);
		Type type = parseResult.Success ? parseResult.Expression?.GetExpectedType() : null;
		if (type == null) yield break; // the expression shows the error

		yield return new KeyValuePair<string, Type>(variableName, type == TypeRules.NULL ? typeof(object) : type);
	}

	protected override DataContext AddVariable(DataContext context) {
		if (valueExpression == null) throw new EvaluationException($"The value of the variable '{variableName}' is invalid", null);

		object variableValue = valueExpression.Evaluate(context);
		if (valueAnimation != null) variableValue = valueAnimation.Update(variableValue, context);

		DataContext variableContext = new DataContext(context);
		variableContext.SetLocal(variableName, variableValue);
		return variableContext;
	}
}
}
