using UnityEngine;
using UnityEngine.UI;

namespace UIConstraints.Unity.Constraints {
// a RawImage shows any texture (e.g. a render texture or a downloaded image), unlike Image, which needs a sprite
[AddComponentMenu("UI Constraints/Raw Image Constraint")]
[RequireComponent(typeof(RawImage))]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/raw-image.png")]
public class RawImageConstraint : ComponentConstraint<RawImage> {
	[SerializeField, ResultType(typeof(Texture))] private CodeInput texture;
	[SerializeField, ResultType(typeof(Color))] private CodeInput color;
	[SerializeField, ResultType(typeof(Rect)), Tooltip("The part of the texture that is shown, in texture coordinates (0 to 1)")] private CodeInput uvRect;

	protected override void Bind() {
		BindProperty<Texture>(nameof(texture), i => i.texture, (i, value) => i.texture = value);
		BindProperty<Color>(nameof(color), i => i.color, (i, value) => i.color = value);
		BindProperty<Rect>(nameof(uvRect), i => i.uvRect, (i, value) => i.uvRect = value);
	}
}
}
