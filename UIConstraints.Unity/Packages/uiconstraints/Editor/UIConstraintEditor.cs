using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UIConstraints.Core.Parsing;
using UIConstraints.Unity.Constraints;
using UnityEditor;
using UnityEngine;

namespace UIConstraints.Unity.Inspector {
// the inspector of all constraints: the default one, plus the variables a constraint provides and problems that are not shown below the inputs already
[CustomEditor(typeof(UIConstraint), true)]
[CanEditMultipleObjects]
public class UIConstraintEditor : Editor {
	private float suggestionsSpace; // empty space at the end, for suggestions of code inputs that reach beyond the inspector, as they would be cut off there

	public override void OnInspectorGUI() {
		// the suggestions of code inputs are drawn last, so the fields after them don't cover them (see CodeArea.deferSuggestions)
		CodeArea.ClearPendingSuggestions();
		CodeArea.deferSuggestions = true;
		try {
			DrawInspector();
		}
		finally {
			CodeArea.deferSuggestions = false;
		}

		GUILayout.Space(suggestionsSpace);
		if (Event.current.type != EventType.Repaint) return;

		float contentEnd = GUILayoutUtility.GetLastRect().y; // the start of the space
		float needed = Mathf.Max(0, CodeArea.DrawPendingSuggestions() + 4 - contentEnd);
		if (Mathf.Approximately(needed, suggestionsSpace)) return;
		suggestionsSpace = needed; // the space has to be the same in all events of a frame, so it is changed for the next one
		Repaint();
	}

	// the default inspector, plus the usings all code inputs share right after the script
	private void DrawInspector() {
		serializedObject.Update();
		bool hasCodeInputs = CodeInputs.GetFields(target.GetType()).Count > 0;
		SerializedProperty property = serializedObject.GetIterator();
		for (bool enterChildren = true; property.NextVisible(enterChildren); enterChildren = false) {
			using (new EditorGUI.DisabledScope(property.propertyPath == "m_Script")) EditorGUILayout.PropertyField(property, true);
			if (property.propertyPath == "m_Script" && hasCodeInputs) DrawSharedUsings();
		}

		serializedObject.ApplyModifiedProperties();
		if (targets.Length != 1) return;

		UIConstraint constraint = (UIConstraint)target;
		if (constraint is IVariableProvider provider) {
			Dictionary<string, Type> availableVariables = VariableScopes.GetAvailableVariables(constraint);
			List<string> variables = provider.GetDeclaredVariables(availableVariables).Select(v => $"'{v.Key}' ({TypeNames.GetDisplayName(v.Value)})").ToList();
			if (variables.Count > 0) EditorGUILayout.HelpBox($"Provides {string.Join(", ", variables)} to {(provider.ProvidesToOwnGameObject ? "the components below and all children" : "the children")}", MessageType.None);
		}

		foreach (ConstraintProblem problem in ConstraintChecks.Check(constraint, false)) EditorGUILayout.HelpBox(problem.message, ConstraintChecks.ToMessageType(problem.severity));
	}

	// the usings of all code inputs (see ISharedUsings), shown once instead of for every input
	private void DrawSharedUsings() {
		SerializedProperty usings = serializedObject.FindProperty("usings");
		MoveOwnUsings(usings);

		List<string> current = CodeInputDrawer.ReadUsings(usings);
		EditorGUI.showMixedValue = usings.hasMultipleDifferentValues;
		List<string> edited = CodeInputDrawer.UsingsField(EditorGUILayout.GetControlRect(), current);
		EditorGUI.showMixedValue = false;
		if (!edited.SequenceEqual(current)) CodeInputDrawer.WriteUsings(usings, edited);
		EditorGUILayout.Space(4);
	}

	// moves the usings the inputs have on their own (from before they were shared) to the shared ones, so they are not hidden
	private void MoveOwnUsings(SerializedProperty sharedUsings) {
		if (targets.Length != 1) return; // the shared usings of several components can't be merged into one list

		List<string> shared = CodeInputDrawer.ReadUsings(sharedUsings);
		foreach (FieldInfo field in CodeInputs.GetFields(target.GetType())) {
			SerializedProperty ownUsings = serializedObject.FindProperty(field.Name)?.FindPropertyRelative("usings");
			if (ownUsings == null || ownUsings.arraySize == 0) continue;

			shared.AddRange(CodeInputDrawer.ReadUsings(ownUsings).Where(u => !shared.Contains(u)));
			ownUsings.arraySize = 0;
		}

		CodeInputDrawer.WriteUsings(sharedUsings, shared);
	}
}
}
