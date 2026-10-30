using UnityEngine;
using UnityEngine.UI;

namespace UIConstraints.Unity.Constraints {
// for the Shadow and Outline effects (Outline extends Shadow)
[AddComponentMenu("UI Constraints/Shadow Constraint")]
[RequireComponent(typeof(Shadow))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/shadow.png")]
public class ShadowConstraint : ComponentConstraint<Shadow> {
	[SerializeField, ResultType(typeof(Color))] private CodeInput effectColor;
	[SerializeField, ResultType(typeof(Vector2))] private CodeInput effectDistance;

	protected override void Bind() {
		BindProperty<Color>(nameof(effectColor), s => s.effectColor, (s, value) => s.effectColor = value);
		BindProperty<Vector2>(nameof(effectDistance), s => s.effectDistance, (s, value) => s.effectDistance = value);
	}
}
}
