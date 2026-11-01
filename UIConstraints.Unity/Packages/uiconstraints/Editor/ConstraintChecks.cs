using System;
using System.Collections.Generic;
using System.Reflection;
using UIConstraints.Core.Animation;
using UIConstraints.Core.Parsing;
using UIConstraints.Unity.Animation;
using UIConstraints.Unity.Constraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UIConstraints.Unity.Inspector {
public enum ProblemSeverity {
	Info,
	Warning,
	Error
}

public readonly struct ConstraintProblem {
	public readonly ProblemSeverity severity;
	public readonly string message;

	public ConstraintProblem(ProblemSeverity severity, string message) {
		this.severity = severity;
		this.message = message;
	}
}

// finds the problems of constraints that can be found in the editor: invalid expressions and variables that are not provided as declared
public static class ConstraintChecks {
	// includeExpressionErrors: whether to include the errors of the expressions, which the inspector already shows below each input
	public static List<ConstraintProblem> Check(UIConstraint constraint, bool includeExpressionErrors) {
		List<ConstraintProblem> problems = new List<ConstraintProblem>();
		Dictionary<string, Type> availableVariables = VariableScopes.GetAvailableVariables(constraint);

		if (includeExpressionErrors)
			foreach (FieldInfo field in CodeInputs.GetFields(constraint.GetType())) {
				ParseResult parseResult = CodeInputs.Parse(constraint, field, availableVariables);
				if (parseResult.Success) continue;

				string fieldName = ObjectNames.NicifyVariableName(field.Name);
				for (int i = 0; i < parseResult.Exception.MessageCount(); i++) problems.Add(new ConstraintProblem(ProblemSeverity.Error, $"{fieldName}: {parseResult.Exception.GetMessage(i)}"));
			}

		CheckTransitions(constraint, availableVariables, includeExpressionErrors, problems);

		string providedName = constraint switch {
			VariableConstraint variableConstraint => variableConstraint.VariableName,
			ForEachConstraint forEachConstraint => forEachConstraint.VariableName,
			_ => null
		};
		if (constraint is IVariableProvider && !IdentifierToken.IsValidIdentifier(providedName))
			problems.Add(new ConstraintProblem(ProblemSeverity.Error, $"'{providedName}' is not a valid variable name (letters, digits and underscores, not starting with a digit)"));

		if (constraint is ExpectedVariable expectedVariable) CheckExpectedVariable(expectedVariable, availableVariables, problems);
		return problems;
	}

	// transitions that can't animate their value (e.g. because the type of a variable changed), and errors in their 'animate from' expressions
	private static void CheckTransitions(UIConstraint constraint, Dictionary<string, Type> availableVariables, bool includeExpressionErrors, List<ConstraintProblem> problems) {
		UnityBlending.EnsureRegistered();
		foreach (FieldInfo field in CodeInputs.GetFields(constraint.GetType())) {
			Transition transition = ((CodeInput)field.GetValue(constraint))?.transition;
			if (transition == null) continue;

			string fieldName = ObjectNames.NicifyVariableName(field.Name);
			Type animatedType = (constraint as IAnimatedInputs)?.GetAnimatedType(field);
			if (animatedType == null) {
				problems.Add(new ConstraintProblem(ProblemSeverity.Warning, $"{fieldName} has an animation, but can't be animated"));
				continue;
			}

			if (!transition.CanAnimate(animatedType)) problems.Add(new ConstraintProblem(ProblemSeverity.Error, $"{fieldName} can't be animated with {Transition.GetDisplayName(transition.GetType())}, as it is {TypeNames.GetDisplayName(animatedType)}"));
			else if (transition is ImageFadeTransition fade && fade.GetParameters(0.5f).shader != null && constraint.TryGetComponent(out UnityEngine.UI.Graphic graphic) && !FadeGraphic.CanDraw(graphic))
				problems.Add(new ConstraintProblem(ProblemSeverity.Warning, $"{fieldName} crossfades instead of using {Transition.GetDisplayName(transition.GetType())}, which only works for simple images"));
			if (!includeExpressionErrors) continue; // shown below the input

			ParseResult animateFrom = CodeInputs.ParseAnimateFrom(constraint, field, availableVariables);
			if (animateFrom != null && !animateFrom.Success)
				for (int i = 0; i < animateFrom.Exception.MessageCount(); i++) problems.Add(new ConstraintProblem(ProblemSeverity.Error, $"{fieldName} (animate from): {animateFrom.Exception.GetMessage(i)}"));
		}
	}

	private static void CheckExpectedVariable(ExpectedVariable expectedVariable, Dictionary<string, Type> availableVariables, List<ConstraintProblem> problems) {
		string name = expectedVariable.VariableName;
		Type expectedType = expectedVariable.GetExpectedType(availableVariables);
		if (expectedType == null || !IdentifierToken.IsValidIdentifier(name)) return; // reported by the type input or the name check

		string expectedTypeName = TypeNames.GetDisplayName(expectedType);
		if (!availableVariables.TryGetValue(name, out Type providedType)) {
			if (IsEditedOnItsOwn(expectedVariable.gameObject)) problems.Add(new ConstraintProblem(ProblemSeverity.Info, $"Once placed in a scene, a parent has to provide '{name}' ({expectedTypeName})"));
			else problems.Add(new ConstraintProblem(ProblemSeverity.Warning, $"No parent provides '{name}' ({expectedTypeName})"));
			return;
		}

		if (TypeRules.IsImplicitlyConvertible(providedType, expectedType)) return;
		string providedTypeName = TypeNames.GetDisplayName(providedType);
		if (TypeRules.IsReferenceConvertible(providedType, expectedType)) problems.Add(new ConstraintProblem(ProblemSeverity.Warning, $"A parent provides '{name}' as {providedTypeName}, which might not be {expectedTypeName}"));
		else problems.Add(new ConstraintProblem(ProblemSeverity.Error, $"A parent provides '{name}' as {providedTypeName}, which can never be {expectedTypeName}"));
	}

	// prefabs that are edited on their own (in prefab mode or as an asset) don't have the parents they will have once placed somewhere
	private static bool IsEditedOnItsOwn(GameObject gameObject) {
		return EditorUtility.IsPersistent(gameObject) || PrefabStageUtility.GetPrefabStage(gameObject) != null;
	}

	public static MessageType ToMessageType(ProblemSeverity severity) {
		return severity switch {
			ProblemSeverity.Error => MessageType.Error,
			ProblemSeverity.Warning => MessageType.Warning,
			_ => MessageType.Info
		};
	}
}
}
