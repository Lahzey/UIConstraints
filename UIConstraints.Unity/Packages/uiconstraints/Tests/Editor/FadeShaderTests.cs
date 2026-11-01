using NUnit.Framework;
using UIConstraints.Core.Animation;
using UIConstraints.Unity.Animation;
using UnityEngine;
using UnityEngine.UI;

namespace UIConstraints.Unity.Tests {
/**
 * The fade shaders compute what ImageFadeTransition.Blend in Core does, so fades look the same in every engine: each shader draws a fade into a render texture,
 * which is compared with Blend at every pixel. The softness is high, as the distance fields and noise are 8 bit textures, which steep edges would amplify.
 */
public class FadeShaderTests {
	private const int SIZE = 32;
	private const float TOLERANCE = 0.04f;

	private Texture2D oldTexture;
	private Texture2D newTexture;
	private Sprite oldSprite;
	private Sprite newSprite;
	private GameObject owner;

	[SetUp]
	public void SetUp() {
		UnityBlending.EnsureRegistered();
		// a red disc with a soft edge, and a blue ring with a green center
		oldTexture = CreateTexture((x, y) => {
			float distance = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f));
			return new Color(1, 0.2f, 0, Mathf.Clamp01(13 - distance));
		});
		newTexture = CreateTexture((x, y) => {
			float distance = Mathf.Max(Mathf.Abs(x - 15.5f), Mathf.Abs(y - 12.5f));
			return distance < 5 ? new Color(0, 1, 0, 0.6f) : distance < 11 ? new Color(0, 0.3f, 1, 1) : Color.clear;
		});
		oldSprite = Sprite.Create(oldTexture, new Rect(0, 0, SIZE, SIZE), Vector2.one / 2, 100, 0, SpriteMeshType.FullRect);
		newSprite = Sprite.Create(newTexture, new Rect(0, 0, SIZE, SIZE), Vector2.one / 2, 100, 0, SpriteMeshType.FullRect);
		owner = new GameObject("image", typeof(RectTransform), typeof(Image));
		((RectTransform)owner.transform).sizeDelta = new Vector2(SIZE, SIZE);
	}

	[TearDown]
	public void TearDown() {
		Object.DestroyImmediate(owner);
		Object.DestroyImmediate(oldSprite);
		Object.DestroyImmediate(newSprite);
		Object.DestroyImmediate(oldTexture);
		Object.DestroyImmediate(newTexture);
	}

	private static Texture2D CreateTexture(System.Func<int, int, Color> color) {
		Texture2D texture = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
		for (int y = 0; y < SIZE; y++) {
			for (int x = 0; x < SIZE; x++) texture.SetPixel(x, y, color(x, y));
		}

		texture.Apply();
		return texture;
	}

	[TestCase(0.25f)]
	[TestCase(0.6f)]
	public void UnionFadesMatchCore(float progress) {
		AssertMatchesCore(new UnionFadeTransition(), progress);
	}

	[TestCase(0.3f, NoiseKind.Clouds)]
	[TestCase(0.7f, NoiseKind.Pixels)]
	public void DissolvesMatchCore(float progress, NoiseKind pattern) {
		AssertMatchesCore(new DissolveTransition { pattern = pattern, scale = 4, softness = 0.3f }, progress);
	}

	[TestCase(0.3f)]
	[TestCase(0.5f)]
	[TestCase(0.8f)]
	public void EdgeRevealsMatchCore(float progress) {
		AssertMatchesCore(new EdgeRevealTransition { softness = 0.2f, overlap = 0.5f }, progress);
	}

	private void AssertMatchesCore(ImageFadeTransition transition, float progress) {
		FadeParameters parameters = transition.GetParameters(progress);
		Color[] rendered = Render(parameters);

		DistanceField oldDistance = ImageFading.GetDistanceField(oldSprite);
		DistanceField newDistance = ImageFading.GetDistanceField(newSprite);
		int compared = 0;
		for (int y = 0; y < SIZE; y++) {
			for (int x = 0; x < SIZE; x++) {
				float u = (x + 0.5f) / SIZE;
				float v = (y + 0.5f) / SIZE;
				FadePoint point = new FadePoint {
					oldColor = ToFadeColor(oldTexture.GetPixel(x, y)),
					newColor = ToFadeColor(newTexture.GetPixel(x, y)),
					oldDistance = oldDistance.Sample(u, v),
					newDistance = newDistance.Sample(u, v),
					noise = parameters.noise?.Sample(u, v) ?? 0
				};
				FadeColor expected = transition.Blend(point, parameters);

				// drawn with alpha blending over transparent black: the color is multiplied by the alpha, and so is the alpha
				Color actual = rendered[y * SIZE + x];
				string where = $"{transition.GetType().Name} at {progress}, pixel {x}, {y}";
				Assert.That(actual.a, Is.EqualTo(expected.a * expected.a).Within(TOLERANCE), where);
				Assert.That(actual.r, Is.EqualTo(expected.r * expected.a).Within(TOLERANCE), where);
				Assert.That(actual.g, Is.EqualTo(expected.g * expected.a).Within(TOLERANCE), where);
				Assert.That(actual.b, Is.EqualTo(expected.b * expected.a).Within(TOLERANCE), where);
				if (expected.a > 0.1f && expected.a < 0.9f) compared++;
			}
		}

		Assert.That(compared, Is.GreaterThan(10), "barely any pixels in the middle of the fade"); // so the test compares more than the images themselves
	}

	private static FadeColor ToFadeColor(Color color) => new FadeColor(color.r, color.g, color.b, color.a);

	// the fade drawn by a FadeGraphic over an image filling a canvas, rendered by a camera into a render texture of the size of the images
	private Color[] Render(FadeParameters parameters) {
		RenderTexture target = RenderTexture.GetTemporary(SIZE, SIZE, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
		GameObject cameraObject = new GameObject("camera", typeof(Camera));
		Camera camera = cameraObject.GetComponent<Camera>();
		camera.clearFlags = CameraClearFlags.SolidColor;
		camera.backgroundColor = Color.clear;
		camera.orthographic = true;
		camera.targetTexture = target;
		camera.enabled = false;

		Canvas canvas = new GameObject("canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceCamera;
		canvas.worldCamera = camera;
		canvas.planeDistance = 1;
		owner.transform.SetParent(canvas.transform, false);
		RectTransform ownerTransform = (RectTransform)owner.transform;
		ownerTransform.anchorMin = Vector2.zero;
		ownerTransform.anchorMax = Vector2.one;
		ownerTransform.sizeDelta = Vector2.zero;
		Image image = owner.GetComponent<Image>();
		image.enabled = false; // only the fade is drawn

		FadeGraphic graphic = new GameObject("fade", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<FadeGraphic>();
		RectTransform graphicTransform = graphic.rectTransform;
		graphicTransform.SetParent(owner.transform, false);
		graphicTransform.anchorMin = Vector2.zero;
		graphicTransform.anchorMax = Vector2.one;
		graphicTransform.sizeDelta = Vector2.zero;

		Texture2D pixels = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, true);
		RenderTexture previous = RenderTexture.active;
		try {
			Canvas.ForceUpdateCanvases();
			Assert.That(FadeGraphic.TryPlace(image, oldSprite, out FadeGraphic.Placement oldPlacement));
			Assert.That(FadeGraphic.TryPlace(image, newSprite, out FadeGraphic.Placement newPlacement));
			Assert.That(newPlacement.rect, Is.EqualTo(new Rect(-SIZE / 2f, -SIZE / 2f, SIZE, SIZE)));
			Texture oldDistance = parameters.usesDistanceFields ? UnityImageFading.GetDistanceTexture(oldSprite) : null;
			Texture newDistance = parameters.usesDistanceFields ? UnityImageFading.GetDistanceTexture(newSprite) : null;
			Texture noise = parameters.noise != null ? UnityImageFading.GetNoiseTexture(parameters.noise) : null;
			graphic.Show(UnityImageFading.FindShader(parameters.shader), oldPlacement, newPlacement, parameters, oldDistance, newDistance, noise);
			Canvas.ForceUpdateCanvases();
			camera.Render();

			RenderTexture.active = target;
			pixels.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0, false);
			return pixels.GetPixels();
		}
		finally {
			RenderTexture.active = previous;
			camera.targetTexture = null;
			RenderTexture.ReleaseTemporary(target);
			Object.DestroyImmediate(pixels);
			Object.DestroyImmediate(graphic.gameObject);
			owner.transform.SetParent(null, false);
			Object.DestroyImmediate(canvas.gameObject);
			Object.DestroyImmediate(cameraObject);
		}
	}
}
}
