using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UnityEngine;

namespace UIConstraints.Unity.Constraints {
/**
 * Declares a variable that a parent has to provide, e.g. the item of a ForEachConstraint for the children of a prefab that is used as its template.
 * Children can use the variable as if it was provided, which they could not while editing the prefab on its own otherwise.
 * At runtime this checks that a parent provides the variable with a fitting value, the editor warns about parents that don't.
 */
[AddComponentMenu("UI Constraints/Expected Variable")]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/expected-variable.png")]
public class ExpectedVariable : VariableConstraint {
	[SerializeField] private string variableName;
	[SerializeField, TypeInput] private CodeInput type;

	[CanBeNull] private Type expectedType;

	public override string VariableName => variableName;

	protected override void Awake() {
		base.Awake();
		expectedType = ParseField(nameof(type))?.GetReferencedType();
	}

	// the type the variable has to be, null if the type is invalid
	[CanBeNull]
	public Type GetExpectedType(IReadOnlyDictionary<string, Type> availableVariables) {
		ParseResult parseResult = CodeInputs.Parse(this, CodeInputs.GetField(GetType(), nameof(type)), availableVariables);
		return parseResult.Success ? parseResult.Expression?.GetReferencedType() : null;
	}

	public override IEnumerable<KeyValuePair<string, Type>> GetDeclaredVariables(IReadOnlyDictionary<string, Type> availableVariables) {
		if (!IdentifierToken.IsValidIdentifier(variableName)) yield break;

		Type declaredType = GetExpectedType(availableVariables);
		if (declaredType != null) yield return new KeyValuePair<string, Type>(variableName, declaredType);
	}

	protected override DataContext AddVariable(DataContext context) {
		if (expectedType == null) throw new EvaluationException($"The type of the expected variable '{variableName}' is invalid", null);
		if (!context.TryGet(variableName, out object value)) throw new EvaluationException($"No parent provides the variable '{variableName}'", null);

		bool fitsType = value == null ? TypeRules.CanBeNull(expectedType) : expectedType.IsInstanceOfType(value);
		if (!fitsType) throw new EvaluationException($"The variable '{variableName}' is expected to be {TypeNames.GetDisplayName(expectedType)}, but is {(value == null ? "null" : TypeNames.GetDisplayName(value.GetType()))}", null);
		return context;
	}
}
}
