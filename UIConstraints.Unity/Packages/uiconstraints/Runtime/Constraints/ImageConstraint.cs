using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace UIConstraints.Unity.Constraints {
[AddComponentMenu("UI Constraints/Image Constraint")]
[RequireComponent(typeof(Image))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/image.png")]
public class ImageConstraint : ComponentConstraint<Image> {
	[SerializeField, ResultType(typeof(Sprite)), FormerlySerializedAs("imageConstraint")] private CodeInput sprite;
	[SerializeField, ResultType(typeof(Color))] private CodeInput color;
	[SerializeField, ResultType(typeof(float)), Tooltip("0 to 1, for images of the type Filled (e.g. health bars or cooldowns)")] private CodeInput fillAmount;
	[SerializeField, ResultType(typeof(bool))] private CodeInput preserveAspect;

	protected override void Bind() {
		BindProperty<Sprite>(nameof(sprite), i => i.sprite, (i, value) => i.sprite = value);
		BindProperty<Color>(nameof(color), i => i.color, (i, value) => i.color = value);
		BindProperty<float>(nameof(fillAmount), i => i.fillAmount, (i, value) => i.fillAmount = value);
		BindProperty<bool>(nameof(preserveAspect), i => i.preserveAspect, (i, value) => i.preserveAspect = value);
	}
}
}
