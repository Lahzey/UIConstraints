using System.Collections;
using NUnit.Framework;
using TMPro;
using UIConstraints.Core.Animation;
using UIConstraints.Unity.Animation;
using UIConstraints.Unity.Constraints;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using static UIConstraints.Unity.Tests.TestHelpers;

namespace UIConstraints.Unity.Tests {
// transitions of the inputs of constraints (see CodeInput.transition)
public class AnimationTests {
	private GameObject root;

	[TearDown]
	public void TearDown() {
		if (root != null) Object.Destroy(root);
	}

	// sets the transition of an input of a constraint
	private static void Animate(Component constraint, string fieldName, Transition transition) {
		CodeInput input = (CodeInput)constraint.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(constraint);
		input.transition = transition;
	}

	[UnityTest]
	public IEnumerator PropertiesMoveToNewValues() {
		TestData.volume = 0;
		root = new GameObject("root");
		root.SetActive(false);
		CanvasGroup group = root.AddComponent<CanvasGroup>();
		CanvasGroupConstraint constraint = AddConstraint<CanvasGroupConstraint>(root, "alpha", "TestData.volume");
		Animate(constraint, "alpha", new InterpolateTransition { duration = 0.5f, curve = TransitionCurve.Linear() });

		root.SetActive(true);
		yield return null;
		Assert.That(group.alpha, Is.EqualTo(0f));

		TestData.volume = 1;
		yield return new WaitForSecondsRealtime(0.25f);
		Assert.That(group.alpha, Is.GreaterThan(0.2f).And.LessThan(0.9f)); // on its way
		yield return new WaitForSecondsRealtime(0.4f);
		Assert.That(group.alpha, Is.EqualTo(1f));
	}

	[UnityTest]
	public IEnumerator ValuesChangedByTheUserAreNotAnimated() {
		TestData.volume = 0;
		root = new GameObject("slider");
		root.SetActive(false);
		Slider slider = root.AddComponent<Slider>();
		SliderConstraint constraint = AddConstraint<SliderConstraint>(root, "value", "TestData.volume", "onValueChanged", "TestData.volume = value");
		Animate(constraint, "value", new InterpolateTransition { duration = 10 });

		root.SetActive(true);
		yield return null;
		slider.value = 0.8f; // like dragging it
		yield return null;
		Assert.That(slider.value, Is.EqualTo(0.8f));

		TestData.volume = 0.2f; // changed elsewhere, which is animated
		yield return null;
		yield return null;
		Assert.That(slider.value, Is.GreaterThan(0.2f));
	}

	[UnityTest]
	public IEnumerator AnimatedVariablesAnimateEverythingUsingThem() {
		TestData.volume = 0;
		root = new GameObject("root");
		root.SetActive(false);
		Variable shown = AddVariable<Variable>(root, "shown", "value", "TestData.volume");
		Animate(shown, "value", new InterpolateTransition { duration = 0.5f, curve = TransitionCurve.Linear(), animateFrom = "1" });
		RecordingConstraint recording = AddRecording(root, "shown");

		root.SetActive(true);
		yield return null;
		Assert.That((float)recording.LastValue, Is.GreaterThan(0.5f)); // animates from 1 to the first value
		yield return new WaitForSecondsRealtime(0.6f);
		Assert.That(recording.LastValue, Is.EqualTo(0f));
	}

	[UnityTest]
	public IEnumerator NumbersInTextCount() {
		TestData.clicked = 10;
		root = new GameObject("root");
		root.SetActive(false);
		TextMeshProUGUI text = root.AddComponent<TextMeshProUGUI>();
		TextConstraint constraint = AddConstraint<TextConstraint>(root, "text", "TestData.clicked + \" gold\"");
		Animate(constraint, "text", new NumbersInTextTransition { duration = 0.5f, fullDurationFrom = 0, curve = TransitionCurve.Linear() });

		root.SetActive(true);
		yield return null;
		TestData.clicked = 110;
		yield return new WaitForSecondsRealtime(0.25f);
		int shown = int.Parse(text.text.Split(' ')[0]);
		Assert.That(shown, Is.GreaterThan(10).And.LessThan(110));
		Assert.That(text.text, Does.EndWith(" gold"));
	}

	// an image showing TestData.sprites[TestData.clicked], fading with the transition
	private Image FadingImage(ImageFadeTransition transition, Image.Type type = Image.Type.Simple) {
		Texture2D texture = Texture2D.whiteTexture;
		TestData.sprites = new[] { Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero), Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero) };
		TestData.clicked = 0;
		root = new GameObject("image", typeof(RectTransform));
		root.SetActive(false);
		Image image = root.AddComponent<Image>();
		image.type = type;
		root.AddComponent<VerticalLayoutGroup>();
		ImageConstraint constraint = AddConstraint<ImageConstraint>(root, "sprite", "TestData.sprites[TestData.clicked]");
		transition.duration = 0.5f;
		transition.curve = TransitionCurve.Linear();
		Animate(constraint, "sprite", transition);
		root.SetActive(true);
		return image;
	}

	[UnityTest]
	public IEnumerator CrossfadesFadeBothSprites() {
		Image image = FadingImage(new CrossfadeTransition());
		yield return null;
		Assert.That(root.transform.childCount, Is.Zero); // nothing to fade yet

		TestData.clicked = 1;
		yield return new WaitForSecondsRealtime(0.2f);
		Assert.That(image.sprite, Is.EqualTo(TestData.sprites[1])); // the image has the new sprite right away, but is hidden while the fade is drawn over it
		Assert.That(image.canvasRenderer.GetAlpha(), Is.Zero);
		Image lower = root.transform.GetChild(0).GetComponent<Image>();
		Image upper = root.transform.GetChild(1).GetComponent<Image>();
		Assert.That(lower.sprite, Is.EqualTo(TestData.sprites[0]));
		Assert.That(upper.sprite, Is.EqualTo(TestData.sprites[1]));
		Assert.That(lower.color.a, Is.GreaterThan(0f).And.LessThan(1f));
		Assert.That(upper.color.a, Is.EqualTo(1 - lower.color.a).Within(0.01f));
		Assert.That(lower.GetComponent<LayoutElement>().ignoreLayout, Is.True);

		yield return new WaitForSecondsRealtime(0.4f);
		Assert.That(lower.gameObject.activeSelf, Is.False);
		Assert.That(upper.gameObject.activeSelf, Is.False);
		Assert.That(image.canvasRenderer.GetAlpha(), Is.EqualTo(1));
	}

	[UnityTest]
	public IEnumerator ShaderFadesAreDrawnByAFadeGraphic() {
		Image image = FadingImage(new EdgeRevealTransition());
		yield return null;
		TestData.clicked = 1;
		yield return new WaitForSecondsRealtime(0.2f);
		FadeGraphic fade = root.GetComponentInChildren<FadeGraphic>(true);
		Assert.That(fade, Is.Not.Null);
		Assert.That(fade.gameObject.activeSelf, Is.True);
		Assert.That(fade.mainTexture, Is.EqualTo(TestData.sprites[1].texture));
		Assert.That(fade.material.shader.name, Is.EqualTo("UIConstraints/Fade/EdgeReveal"));
		Assert.That(image.canvasRenderer.GetAlpha(), Is.Zero);

		yield return new WaitForSecondsRealtime(0.4f);
		Assert.That(fade.gameObject.activeSelf, Is.False);
		Assert.That(image.canvasRenderer.GetAlpha(), Is.EqualTo(1));
	}

	[UnityTest]
	public IEnumerator FilledImagesCrossfadeInstead() {
		FadingImage(new UnionFadeTransition(), Image.Type.Filled);
		yield return null;
		TestData.clicked = 1;
		yield return new WaitForSecondsRealtime(0.2f);
		Assert.That(root.GetComponentInChildren<FadeGraphic>(true), Is.Null);
		Assert.That(root.transform.GetChild(1).GetComponent<Image>().sprite, Is.EqualTo(TestData.sprites[1]));
	}
}
}
