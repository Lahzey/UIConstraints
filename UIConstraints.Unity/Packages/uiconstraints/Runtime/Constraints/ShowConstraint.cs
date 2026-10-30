using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;
using UnityEngine;
using UnityEngine.Serialization;

namespace UIConstraints.Unity.Constraints {
// shows the game object only while the condition is true (unlike other constraints, this is also rendered while the game object is inactive, so it can activate it again)
[AddComponentMenu("UI Constraints/Show Constraint")]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/show.png")]
public class ShowConstraint : UIConstraint {
	[SerializeField, ResultType(typeof(bool)), FormerlySerializedAs("conditionConstraint")] private CodeInput condition;
	[SerializeField] private bool disableAtRuntime; // since this constraint works even if disabled, we need another way of disabling it

	private Expression conditionExpression;


	protected override void Awake() {
		base.Awake();

		conditionExpression = ParseField(nameof(condition));
	}

	public override void Render(DataContext context) {
		if (disableAtRuntime) return;
		try {
			object result = conditionExpression?.Evaluate(context) ?? true;
			if (result == null)
				gameObject.SetActive(false);
			else if (result is bool b)
				gameObject.SetActive(b);
			else
				Debug.LogError("Show condition not a boolean: " + result, this);
		}
		catch (EvaluationException e) {
			Debug.LogException(e, this);
		}
	}
}
}