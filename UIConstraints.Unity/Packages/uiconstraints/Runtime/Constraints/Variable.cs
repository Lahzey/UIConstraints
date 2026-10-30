using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;
using UnityEngine;

namespace UIConstraints.Unity.Constraints {
/**
 * Sets a variable to the value of an expression, for the components after it on the same game object and all children.
 * E.g. with 'player' set to 'Player.Instance', children can just use 'player.name'. The value is evaluated once per render, no matter how many expressions use it.
 */
[AddComponentMenu("UI Constraints/Variable")]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/variable.png")]
public class Variable : VariableConstraint {
	[SerializeField] private string variableName;
	[SerializeField] private CodeInput value;

	[CanBeNull] private Expression valueExpression;

	public override string VariableName => variableName;

	protected override void Awake() {
		base.Awake();
		valueExpression = ParseField(nameof(value));
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

		DataContext variableContext = new DataContext(context);
		variableContext.SetLocal(variableName, valueExpression.Evaluate(context));
		return variableContext;
	}
}
}
