using System;
using System.Collections.Generic;
using UnityEngine;

namespace UIConstraints.Unity {
/**
 * A component that provides variables to other components, e.g. the item of a ForEachConstraint for its children.
 * The values are provided at runtime, but the variables and their types have to be known while parsing (in the editor and when the components are created),
 * so expressions using them can be checked and completed.
 */
public interface IVariableProvider {
	// whether the components after this one on the same game object (in component order) can use the variables, or only the children
	bool ProvidesToOwnGameObject { get; }

	/**
	 * The variables this provides with their types, given the variables available to this component itself (as its own expressions may use those).
	 * Called while parsing, so this must not evaluate anything.
	 */
	IEnumerable<KeyValuePair<string, Type>> GetDeclaredVariables(IReadOnlyDictionary<string, Type> availableVariables);
}

/**
 * Determines which variables a component can use, which is the same in the editor and at runtime, as it only depends on the hierarchy:
 * those provided by components on its parents, and those provided by components before it on its own game object (if they provide to their own game object).
 * Variables with the same name hide those provided further up.
 */
public static class VariableScopes {
	public static Dictionary<string, Type> GetAvailableVariables(Component component) {
		List<Transform> path = new List<Transform>();
		for (Transform transform = component.transform; transform != null; transform = transform.parent) path.Add(transform);
		path.Reverse();

		Dictionary<string, Type> variables = new Dictionary<string, Type>();
		foreach (Transform transform in path) {
			bool isOwnGameObject = transform == component.transform;
			Dictionary<string, Type> childrenOnlyVariables = null; // not visible to the following components on the same game object, so they are only added once it is done

			foreach (Component other in transform.GetComponents<Component>()) {
				if (isOwnGameObject && other == component) return variables;
				if (other is not IVariableProvider provider) continue; // also skips missing scripts, which are null
				if (isOwnGameObject && !provider.ProvidesToOwnGameObject) continue;

				foreach (KeyValuePair<string, Type> variable in provider.GetDeclaredVariables(variables)) {
					if (provider.ProvidesToOwnGameObject) variables[variable.Key] = variable.Value;
					else (childrenOnlyVariables ??= new Dictionary<string, Type>())[variable.Key] = variable.Value;
				}
			}

			if (childrenOnlyVariables != null)
				foreach (KeyValuePair<string, Type> variable in childrenOnlyVariables)
					variables[variable.Key] = variable.Value;
		}

		return variables;
	}
}
}
