using System.Collections.Generic;
using System.Linq;
using UIConstraints.Unity.Constraints;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UIConstraints.Unity.Inspector {
/**
 * Shows icons in the hierarchy window for game objects whose constraints have problems (see ConstraintChecks), with the problems as tooltip.
 * Parents of such game objects get a faded icon, so problems are visible even if they are collapsed.
 * Checking is separate from drawing (which Unity only does for visible rows): all constraints of the open scenes and prefab are checked whenever something changes.
 */
[InitializeOnLoad]
internal static class HierarchyValidation {
	private class ObjectProblems {
		public readonly List<string> messages = new List<string>(); // of the game object itself
		public ProblemSeverity severity; // of the game object itself
		public ProblemSeverity childrenSeverity;
		public bool hasChildProblems;
	}

	private static readonly Dictionary<int, ObjectProblems> PROBLEMS = new Dictionary<int, ObjectProblems>(); // <game object instance id, problems>
	private static bool dirty = true;

	static HierarchyValidation() {
		ObjectChangeEvents.changesPublished += OnChangesPublished;
		Undo.undoRedoPerformed += MarkDirty;
		EditorApplication.hierarchyChanged += MarkDirty;
		EditorApplication.playModeStateChanged += _ => MarkDirty();
		EditorSceneManager.sceneOpened += (_, _) => MarkDirty();
		EditorSceneManager.sceneClosed += _ => MarkDirty();
		PrefabStage.prefabStageOpened += _ => MarkDirty();
		PrefabStage.prefabStageClosing += _ => MarkDirty();
		EditorApplication.update += ValidateIfDirty;
		EditorApplication.hierarchyWindowItemOnGUI += DrawHierarchyItem;
	}

	private static void OnChangesPublished(ref ObjectChangeEventStream stream) {
		MarkDirty();
	}

	private static void MarkDirty() {
		dirty = true;
	}

	private static void ValidateIfDirty() {
		// constraints change the hierarchy constantly while playing (e.g. ForEachConstraints creating children), there is nothing to edit then anyway
		if (!dirty || EditorApplication.isPlayingOrWillChangePlaymode) return;
		dirty = false;
		Validate();
		EditorApplication.RepaintHierarchyWindow();
	}

	private static void Validate() {
		PROBLEMS.Clear();
		foreach (GameObject root in GetRootGameObjects())
			foreach (UIConstraint constraint in root.GetComponentsInChildren<UIConstraint>(true)) {
				string constraintName = ObjectNames.NicifyVariableName(constraint.GetType().Name);
				foreach (ConstraintProblem problem in ConstraintChecks.Check(constraint, true)) {
					if (problem.severity == ProblemSeverity.Info) continue;

					ObjectProblems objectProblems = GetOrAdd(constraint.gameObject);
					objectProblems.messages.Add($"{constraintName} - {problem.message}");
					if (problem.severity > objectProblems.severity) objectProblems.severity = problem.severity;

					for (Transform parent = constraint.transform.parent; parent != null; parent = parent.parent) {
						ObjectProblems parentProblems = GetOrAdd(parent.gameObject);
						parentProblems.hasChildProblems = true;
						if (problem.severity > parentProblems.childrenSeverity) parentProblems.childrenSeverity = problem.severity;
					}
				}
			}
	}

	private static ObjectProblems GetOrAdd(GameObject gameObject) {
		if (!PROBLEMS.TryGetValue(gameObject.GetInstanceID(), out ObjectProblems problems)) PROBLEMS[gameObject.GetInstanceID()] = problems = new ObjectProblems();
		return problems;
	}

	private static IEnumerable<GameObject> GetRootGameObjects() {
		for (int i = 0; i < SceneManager.sceneCount; i++) {
			Scene scene = SceneManager.GetSceneAt(i);
			if (scene.isLoaded)
				foreach (GameObject root in scene.GetRootGameObjects())
					yield return root;
		}

		PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
		if (prefabStage != null) yield return prefabStage.prefabContentsRoot;
	}

	private static void DrawHierarchyItem(int instanceId, Rect rowRect) {
		if (!PROBLEMS.TryGetValue(instanceId, out ObjectProblems problems)) return;

		bool hasOwnProblems = problems.messages.Count > 0;
		ProblemSeverity severity = hasOwnProblems && problems.severity >= problems.childrenSeverity ? problems.severity : problems.childrenSeverity;
		string tooltip = string.Join("\n", problems.messages.Append(problems.hasChildProblems ? $"Children have {(problems.childrenSeverity == ProblemSeverity.Error ? "errors" : "warnings")}" : null).Where(m => m != null));
		Texture icon = EditorGUIUtility.IconContent(severity == ProblemSeverity.Error ? "console.erroricon.sml" : "console.warnicon.sml").image;

		Color previousColor = GUI.color;
		if (!hasOwnProblems) GUI.color = new Color(1, 1, 1, 0.5f); // only children have problems
		GUI.Label(new Rect(rowRect.xMax - 16, rowRect.y, 16, rowRect.height), new GUIContent(icon, tooltip));
		GUI.color = previousColor;
	}
}
}
