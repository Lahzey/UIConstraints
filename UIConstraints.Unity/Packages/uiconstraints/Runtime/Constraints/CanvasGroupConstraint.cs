using UnityEngine;

namespace UIConstraints.Unity.Constraints {
// affects a whole group of UI elements: fading them out, or making them not react to input
[AddComponentMenu("UI Constraints/Canvas Group Constraint")]
[RequireComponent(typeof(CanvasGroup))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/canvas-group.png")]
public class CanvasGroupConstraint : ComponentConstraint<CanvasGroup> {
	[SerializeField, ResultType(typeof(float)), Tooltip("0 (invisible) to 1")] private CodeInput alpha;
	[SerializeField, ResultType(typeof(bool))] private CodeInput interactable;
	[SerializeField, ResultType(typeof(bool)), Tooltip("Whether the elements of the group can be clicked (or block clicks to what is behind them)")] private CodeInput blocksRaycasts;

	protected override void Bind() {
		BindProperty<float>(nameof(alpha), g => g.alpha, (g, value) => g.alpha = value);
		BindProperty<bool>(nameof(interactable), g => g.interactable, (g, value) => g.interactable = value);
		BindProperty<bool>(nameof(blocksRaycasts), g => g.blocksRaycasts, (g, value) => g.blocksRaycasts = value);
	}
}
}
