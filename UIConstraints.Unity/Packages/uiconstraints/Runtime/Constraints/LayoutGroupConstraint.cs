using UnityEngine;
using UnityEngine.UI;

namespace UIConstraints.Unity.Constraints {
// for horizontal and vertical layout groups, see GridLayoutGroupConstraint for grids
[AddComponentMenu("UI Constraints/Layout Group Constraint")]
[RequireComponent(typeof(HorizontalOrVerticalLayoutGroup))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/layout-group.png")]
public class LayoutGroupConstraint : ComponentConstraint<HorizontalOrVerticalLayoutGroup> {
	[SerializeField, ResultType(typeof(RectOffset)), Tooltip("e.g. new RectOffset(left, right, top, bottom)")] private CodeInput padding;
	[SerializeField, ResultType(typeof(float))] private CodeInput spacing;
	[SerializeField, ResultType(typeof(TextAnchor))] private CodeInput childAlignment;
	[SerializeField, ResultType(typeof(bool))] private CodeInput reverseArrangement;

	protected override void Bind() {
		BindProperty<RectOffset>(nameof(padding), SetPadding);
		BindProperty<float>(nameof(spacing), l => l.spacing, (l, value) => l.spacing = value);
		BindProperty<TextAnchor>(nameof(childAlignment), l => l.childAlignment, (l, value) => l.childAlignment = value);
		BindProperty<bool>(nameof(reverseArrangement), l => l.reverseArrangement, (l, value) => l.reverseArrangement = value);
	}

	// RectOffsets are compared by reference, so the padding is compared here instead, as setting it rebuilds the layout
	internal static void SetPadding(LayoutGroup layoutGroup, RectOffset padding) {
		RectOffset current = layoutGroup.padding;
		padding ??= new RectOffset();
		if (current.left == padding.left && current.right == padding.right && current.top == padding.top && current.bottom == padding.bottom) return;

		layoutGroup.padding = padding;
	}
}
}
