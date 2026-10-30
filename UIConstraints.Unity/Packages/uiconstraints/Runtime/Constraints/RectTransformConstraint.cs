using UnityEngine;
using UnityEngine.Serialization;

namespace UIConstraints.Unity.Constraints {
/**
 * The layout of a UI element. The anchors are separate numbers, as they are often set on their own, e.g. a bar that fills a part of its parent with anchorMaxX = health / maxHealth.
 */
[AddComponentMenu("UI Constraints/Rect Transform Constraint")]
[RequireComponent(typeof(RectTransform))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/rect-transform.png")]
public class RectTransformConstraint : ComponentConstraint<RectTransform> {
	[SerializeField, ResultType(typeof(float)), FormerlySerializedAs("minXConstraint")] private CodeInput anchorMinX;
	[SerializeField, ResultType(typeof(float)), FormerlySerializedAs("maxXConstraint")] private CodeInput anchorMaxX;
	[SerializeField, ResultType(typeof(float)), FormerlySerializedAs("minYConstraint")] private CodeInput anchorMinY;
	[SerializeField, ResultType(typeof(float)), FormerlySerializedAs("maxYConstraint")] private CodeInput anchorMaxY;
	[SerializeField, ResultType(typeof(Vector2)), Tooltip("The position relative to the anchors")] private CodeInput anchoredPosition;
	[SerializeField, ResultType(typeof(Vector2)), Tooltip("The size relative to the anchors (the size itself if both anchors are at the same point)")] private CodeInput sizeDelta;
	[SerializeField, ResultType(typeof(Vector2))] private CodeInput pivot;
	[SerializeField, ResultType(typeof(float)), Tooltip("The rotation around the z axis in degrees")] private CodeInput rotation;
	[SerializeField, ResultType(typeof(Vector3))] private CodeInput scale;

	protected override void Bind() {
		BindProperty<float>(nameof(anchorMinX), r => r.anchorMin.x, (r, value) => r.anchorMin = new Vector2(value, r.anchorMin.y));
		BindProperty<float>(nameof(anchorMaxX), r => r.anchorMax.x, (r, value) => r.anchorMax = new Vector2(value, r.anchorMax.y));
		BindProperty<float>(nameof(anchorMinY), r => r.anchorMin.y, (r, value) => r.anchorMin = new Vector2(r.anchorMin.x, value));
		BindProperty<float>(nameof(anchorMaxY), r => r.anchorMax.y, (r, value) => r.anchorMax = new Vector2(r.anchorMax.x, value));
		BindProperty<Vector2>(nameof(anchoredPosition), r => r.anchoredPosition, (r, value) => r.anchoredPosition = value);
		BindProperty<Vector2>(nameof(sizeDelta), r => r.sizeDelta, (r, value) => r.sizeDelta = value);
		BindProperty<Vector2>(nameof(pivot), r => r.pivot, (r, value) => r.pivot = value);
		BindProperty<float>(nameof(rotation), r => r.localEulerAngles.z, (r, value) => r.localEulerAngles = new Vector3(r.localEulerAngles.x, r.localEulerAngles.y, value));
		BindProperty<Vector3>(nameof(scale), r => r.localScale, (r, value) => r.localScale = value);
	}
}
}
