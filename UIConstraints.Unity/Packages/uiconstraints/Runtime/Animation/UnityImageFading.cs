using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using UIConstraints.Core.Animation;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UIConstraints.Unity.Animation {
/**
 * Fades the sprites of images and the textures of raw images (see ImageFadeTransition). Fade shaders are found by name (UIConstraints/Fade/{key}),
 * so they have to be in a Resources folder or always included, to be in builds.
 */
public class UnityImageFading : IImageFadeBackend {
	private const string SHADER_PREFIX = "UIConstraints/Fade/";

	private static readonly Dictionary<string, Shader> SHADERS = new Dictionary<string, Shader>();
	private static readonly ConditionalWeakTable<DistanceField, Texture2D> DISTANCE_TEXTURES = new ConditionalWeakTable<DistanceField, Texture2D>();
	private static readonly Dictionary<NoisePattern, Texture2D> NOISE_TEXTURES = new Dictionary<NoisePattern, Texture2D>();

	public bool CanFade(Type valueType) => valueType == typeof(Sprite) || typeof(Texture).IsAssignableFrom(valueType);

	public IImageFader CreateFader(object owner) => owner is Image || owner is RawImage ? new UnityImageFader((Graphic)owner) : null;

	public AlphaImage ReadAlpha(object image, int maxSize) {
		Texture texture;
		Rect area;
		if (image is Sprite sprite && sprite != null && sprite.texture != null) {
			texture = sprite.texture;
			Vector4 outer = DataUtility.GetOuterUV(sprite);
			area = Rect.MinMaxRect(outer.x, outer.y, outer.z, outer.w);
		}
		else if (image is Texture imageTexture && imageTexture != null) {
			texture = imageTexture;
			area = new Rect(0, 0, 1, 1);
		}
		else {
			return null;
		}

		// drawn into a small render texture and read from there, which works for any texture (also ones that aren't readable, compressed or in an atlas)
		float pixelWidth = area.width * texture.width;
		float pixelHeight = area.height * texture.height;
		float scale = Mathf.Min(1, maxSize / Mathf.Max(pixelWidth, pixelHeight, 1));
		int width = Mathf.Max(1, Mathf.RoundToInt(pixelWidth * scale));
		int height = Mathf.Max(1, Mathf.RoundToInt(pixelHeight * scale));
		RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
		RenderTexture previous = RenderTexture.active;
		Texture2D pixels = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
		try {
			Graphics.Blit(texture, target, area.size, area.position);
			RenderTexture.active = target;
			pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
			Color32[] colors = pixels.GetPixels32();
			byte[] alpha = new byte[colors.Length];
			for (int i = 0; i < colors.Length; i++) alpha[i] = colors[i].a;
			return new AlphaImage(width, height, alpha);
		}
		finally {
			RenderTexture.active = previous;
			RenderTexture.ReleaseTemporary(target);
			Object.DestroyImmediate(pixels);
		}
	}

	// the fade shader for a key, null if there is none (which is logged once)
	[CanBeNull]
	public static Shader FindShader(string key) {
		if (SHADERS.TryGetValue(key, out Shader shader) && shader != null) return shader;
		shader = Shader.Find(SHADER_PREFIX + key);
		if (shader == null && !SHADERS.ContainsKey(key)) Debug.LogWarning($"There is no shader {SHADER_PREFIX + key} for image fades, so they crossfade instead. Shaders have to be in a Resources folder or always included to be found.");
		SHADERS[key] = shader;
		return shader;
	}

	// the distance field of an image as a texture, null if it can't be read
	[CanBeNull]
	public static Texture2D GetDistanceTexture(object image) {
		DistanceField field = ImageFading.GetDistanceField(image);
		if (field == null) return null;
		if (DISTANCE_TEXTURES.TryGetValue(field, out Texture2D texture) && texture != null) return texture;

		texture = CreateTexture("Distance Field", field.width, field.height, field.values, TextureWrapMode.Clamp, true);
		DISTANCE_TEXTURES.AddOrUpdate(field, texture);
		return texture;
	}

	public static Texture2D GetNoiseTexture(NoisePattern pattern) {
		if (NOISE_TEXTURES.TryGetValue(pattern, out Texture2D texture) && texture != null) return texture;
		texture = CreateTexture("Noise", NoisePattern.SIZE, NoisePattern.SIZE, pattern.Values, TextureWrapMode.Repeat, pattern.Smooth);
		NOISE_TEXTURES[pattern] = texture;
		return texture;
	}

	private static Texture2D CreateTexture(string name, int width, int height, byte[] values, TextureWrapMode wrapMode, bool smooth) {
		Texture2D texture = new Texture2D(width, height, TextureFormat.R8, false, true) {
			name = "[UIConstraints] " + name,
			hideFlags = HideFlags.HideAndDontSave,
			wrapMode = wrapMode,
			filterMode = smooth ? FilterMode.Bilinear : FilterMode.Point
		};
		texture.SetPixelData(values, 0);
		texture.Apply(false, true);
		return texture;
	}
}

/**
 * Draws the fades of an image or raw image: hides it, and draws the fade in hidden children instead. These ignore layouts and cover the image exactly, so they don't change anything else.
 * They are the first children, so other children stay on top. Fades with a shader are drawn by a FadeGraphic, others (and images FadeGraphic can't draw, e.g. sliced ones)
 * as two copies of the image on top of each other.
 */
public class UnityImageFader : IImageFader {
	private const string OVERLAY_NAME = "[UIConstraints] Fade";

	private readonly Graphic owner;
	private FadeGraphic composite;
	private Graphic lower;
	private Graphic upper;
	private bool hidingOwner;
	private float ownerAlpha;

	public UnityImageFader(Graphic owner) {
		this.owner = owner;
	}

	public void Show(object from, object to, FadeParameters parameters) {
		if (owner == null) return;

		if (!hidingOwner) {
			hidingOwner = true;
			ownerAlpha = owner.canvasRenderer.GetAlpha();
			owner.canvasRenderer.SetAlpha(0);
		}

		if (!TryShowComposite(from, to, parameters)) ShowLayered(from, to, parameters);
	}

	public void Hide() {
		if (composite != null) composite.gameObject.SetActive(false);
		if (lower != null) lower.gameObject.SetActive(false);
		if (upper != null) upper.gameObject.SetActive(false);
		if (hidingOwner && owner != null) owner.canvasRenderer.SetAlpha(ownerAlpha);
		hidingOwner = false;
	}

	private bool TryShowComposite(object from, object to, FadeParameters parameters) {
		if (parameters.shader == null || !FadeGraphic.CanDraw(owner)) return false;
		Shader shader = UnityImageFading.FindShader(parameters.shader);
		if (shader == null || !FadeGraphic.TryPlace(owner, to, out FadeGraphic.Placement newPlacement)) return false;
		bool hasOld = FadeGraphic.TryPlace(owner, from, out FadeGraphic.Placement oldPlacement);

		Texture oldDistance = null;
		Texture newDistance = null;
		if (parameters.usesDistanceFields) {
			newDistance = UnityImageFading.GetDistanceTexture(to);
			oldDistance = hasOld ? UnityImageFading.GetDistanceTexture(from) : null;
			if (newDistance == null || (hasOld && oldDistance == null)) return false;
		}

		Texture noise = parameters.noise != null ? UnityImageFading.GetNoiseTexture(parameters.noise) : null;

		if (lower != null) lower.gameObject.SetActive(false);
		if (upper != null) upper.gameObject.SetActive(false);
		if (composite == null) {
			composite = CreateOverlay(owner).AddComponent<FadeGraphic>();
			composite.raycastTarget = false;
		}

		Prepare(composite);
		composite.Show(shader, oldPlacement, newPlacement, parameters, oldDistance, newDistance, noise);
		return true;
	}

	private void ShowLayered(object from, object to, FadeParameters parameters) {
		if (composite != null) composite.gameObject.SetActive(false);
		if (lower == null) {
			lower = CreateImage(owner);
			upper = CreateImage(owner);
			upper.transform.SetSiblingIndex(1); // above the lower one, below the children of the image
		}

		if (parameters.oldOnTop) {
			ShowLayer(lower, to, parameters.newOpacity);
			ShowLayer(upper, from, parameters.oldOpacity);
		}
		else {
			ShowLayer(lower, from, parameters.oldOpacity);
			ShowLayer(upper, to, parameters.newOpacity);
		}
	}

	private void ShowLayer(Graphic layer, object image, float opacity) {
		Prepare(layer);
		CopyLook(owner, layer);
		Color color = owner.color;
		color.a *= opacity;
		layer.color = color;
		if (layer is Image layerImage) layerImage.sprite = image as Sprite;
		else ((RawImage)layer).texture = image as Texture;
	}

	// the overlay is active and looks like the image (except for what it shows)
	private void Prepare(Graphic overlay) {
		overlay.gameObject.SetActive(true);
		overlay.rectTransform.pivot = owner.rectTransform.pivot; // so its local coordinates are those of the image
		overlay.color = owner.color;
		Color rendererColor = owner.canvasRenderer.GetColor(); // e.g. the tint of a button
		rendererColor.a = ownerAlpha;
		overlay.canvasRenderer.SetColor(rendererColor);
	}

	private static GameObject CreateOverlay(Graphic owner) {
		GameObject gameObject = new GameObject(OVERLAY_NAME, typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement)) {
			hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave
		};
		gameObject.GetComponent<LayoutElement>().ignoreLayout = true;
		RectTransform rectTransform = (RectTransform)gameObject.transform;
		rectTransform.SetParent(owner.transform, false);
		rectTransform.SetAsFirstSibling();
		rectTransform.anchorMin = Vector2.zero;
		rectTransform.anchorMax = Vector2.one;
		rectTransform.offsetMin = Vector2.zero;
		rectTransform.offsetMax = Vector2.zero;
		return gameObject;
	}

	private static Graphic CreateImage(Graphic owner) {
		GameObject gameObject = CreateOverlay(owner);
		Graphic image = owner is Image ? gameObject.AddComponent<Image>() : gameObject.AddComponent<RawImage>();
		image.raycastTarget = false;
		return image;
	}

	private static void CopyLook(Graphic owner, Graphic overlay) {
		overlay.material = owner.material;
		if (owner is Image ownerImage && overlay is Image overlayImage) {
			overlayImage.type = ownerImage.type;
			overlayImage.preserveAspect = ownerImage.preserveAspect;
			overlayImage.fillCenter = ownerImage.fillCenter;
			overlayImage.fillMethod = ownerImage.fillMethod;
			overlayImage.fillOrigin = ownerImage.fillOrigin;
			overlayImage.fillAmount = ownerImage.fillAmount;
			overlayImage.fillClockwise = ownerImage.fillClockwise;
			overlayImage.pixelsPerUnitMultiplier = ownerImage.pixelsPerUnitMultiplier;
			overlayImage.useSpriteMesh = ownerImage.useSpriteMesh;
		}
		else if (owner is RawImage ownerRawImage && overlay is RawImage overlayRawImage) {
			overlayRawImage.uvRect = ownerRawImage.uvRect;
		}
	}
}
}
