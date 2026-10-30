using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;
using UnityEngine;

namespace UIConstraints.Unity.Constraints {
public abstract class UIConstraint : MonoBehaviour, ISharedUsings {
	[SerializeField, HideInInspector] private List<string> usings = new List<string>(); // shared by all CodeInput fields, edited at the top of the inspector

	protected UINode node;

	public IReadOnlyList<string> SharedUsings => usings;

	protected virtual void Awake() {
		node = UINode.AddToNode(this);
	}

	protected virtual void OnDestroy() {
		node.Remove(this);
	}

	public abstract void Render(DataContext context);

	// parses one of the CodeInput fields of this constraint (see CodeInputs.Parse). Returns null if it is empty or invalid, the errors are logged in that case
	[CanBeNull]
	protected Expression ParseField(string fieldName) {
		FieldInfo field = CodeInputs.GetField(GetType(), fieldName);
		ParseResult parseResult = CodeInputs.Parse(this, field);
		if (parseResult.Success) return parseResult.Expression;

		string messages = string.Join("\n", Enumerable.Range(0, parseResult.Exception.MessageCount()).Select(i => parseResult.Exception.GetMessage(i)));
		Debug.LogError($"Invalid {fieldName} of {GetType().Name} '{parseResult.source}':\n{messages}", this);
		return null;
	}
}
}
