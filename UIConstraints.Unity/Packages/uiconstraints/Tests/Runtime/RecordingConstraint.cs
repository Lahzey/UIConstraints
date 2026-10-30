using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing.Expressions;
using UIConstraints.Unity.Constraints;
using UnityEngine;

namespace UIConstraints.Unity.Tests {
// a constraint that remembers what its expression evaluated to, to test what constraints get without depending on UI components
public class RecordingConstraint : UIConstraint {
	[SerializeField] private CodeInput value;

	private Expression valueExpression;

	public object LastValue { get; private set; }
	public int RenderCount { get; private set; }

	protected override void Awake() {
		base.Awake();
		valueExpression = ParseField(nameof(value));
	}

	public override void Render(DataContext context) {
		LastValue = valueExpression?.Evaluate(context);
		RenderCount++;
	}
}
}
