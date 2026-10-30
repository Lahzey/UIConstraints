using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UnityEngine;

namespace UIConstraints.Unity.Constraints {
/**
 * A constraint that provides a variable to the components after it on its game object and to all children.
 * Instead of being rendered, the UINode lets it add its variable to the context, as that changes the context for everything after it.
 */
public abstract class VariableConstraint : UIConstraint, IVariableProvider {
	[CanBeNull] private string loggedError; // errors are logged once instead of on every render

	public abstract string VariableName { get; }

	public bool ProvidesToOwnGameObject => true;

	public abstract IEnumerable<KeyValuePair<string, Type>> GetDeclaredVariables(IReadOnlyDictionary<string, Type> availableVariables);

	/**
	 * Adds the variable to the context. Returns false if that failed, in which case the components after this one and the children are not rendered,
	 * as they would fail too (they may use the variable).
	 */
	public bool TryAddVariable(ref DataContext context) {
		try {
			context = AddVariable(context);
			loggedError = null;
			return true;
		}
		catch (EvaluationException e) {
			if (loggedError != e.Message) Debug.LogException(e, this);
			loggedError = e.Message;
			return false;
		}
	}

	protected abstract DataContext AddVariable(DataContext context);

	// variables are added by the UINode instead (see TryAddVariable)
	public sealed override void Render(DataContext context) { }
}
}
