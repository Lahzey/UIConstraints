using UnityEngine;
using UnityEngine.UI;

namespace UIConstraints.Unity.Constraints {
[AddComponentMenu("UI Constraints/Grid Layout Group Constraint")]
[RequireComponent(typeof(GridLayoutGroup))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/grid-layout-group.png")]
public class GridLayoutGroupConstraint : ComponentConstraint<GridLayoutGroup> {
	[SerializeField, ResultType(typeof(RectOffset)), Tooltip("e.g. new RectOffset(left, right, top, bottom)")] private CodeInput padding;
	[SerializeField, ResultType(typeof(Vector2))] private CodeInput cellSize;
	[SerializeField, ResultType(typeof(Vector2))] private CodeInput spacing;
	[SerializeField, ResultType(typeof(TextAnchor))] private CodeInput childAlignment;
	[SerializeField, ResultType(typeof(int)), Tooltip("The number of columns or rows, if the constraint of the grid is set to a fixed count")] private CodeInput constraintCount;

	protected override void Bind() {
		BindProperty<RectOffset>(nameof(padding), LayoutGroupConstraint.SetPadding);
		BindProperty<Vector2>(nameof(cellSize), g => g.cellSize, (g, value) => g.cellSize = value);
		BindProperty<Vector2>(nameof(spacing), g => g.spacing, (g, value) => g.spacing = value);
		BindProperty<TextAnchor>(nameof(childAlignment), g => g.childAlignment, (g, value) => g.childAlignment = value);
		BindProperty<int>(nameof(constraintCount), g => g.constraintCount, (g, value) => g.constraintCount = value);
	}
}
}
