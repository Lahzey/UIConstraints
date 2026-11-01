using System.Collections.Generic;
using UIConstraints.Core.Animation;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace UIConstraints.Unity.Animation {
/**
 * Draws a fade of an image with its shader (see ImageFadeTransition), over the image, which is hidden meanwhile (see UnityImageFader).
 * One quad covers where both images are drawn (they can differ, e.g. with preserve aspect), the shader finds each image in it (see UIConstraintsFade.cginc).
 * Supports simple images and raw images, as other image types (e.g. sliced) don't draw the whole image in one piece.
 */
[AddComponentMenu("")]
public class FadeGraphic : MaskableGraphic {
	private static readonly Dictionary<string, int> PROPERTY_IDS = new Dictionary<string, int>();
	private static readonly int OLD_TEX = Shader.PropertyToID("_OldTex");
	private static readonly int OLD_MAP = Shader.PropertyToID("_OldMap");
	private static readonly int NEW_MAP = Shader.PropertyToID("_NewMap");
	private static readonly int OLD_UV = Shader.PropertyToID("_OldUV");
	private static readonly int NEW_UV = Shader.PropertyToID("_NewUV");
	private static readonly int OLD_AREA = Shader.PropertyToID("_OldArea");
	private static readonly int NEW_AREA = Shader.PropertyToID("_NewArea");
	private static readonly int OLD_DISTANCE = Shader.PropertyToID("_OldDistance");
	private static readonly int NEW_DISTANCE = Shader.PropertyToID("_NewDistance");
	private static readonly int NOISE = Shader.PropertyToID("_Noise");

	private readonly Dictionary<Shader, Material> materials = new Dictionary<Shader, Material>();
	private Texture newTexture;
	private Rect bounds; // where both images are drawn, in local coordinates

	public override Texture mainTexture => newTexture != null ? newTexture : s_WhiteTexture;

	// where an image is drawn: the rectangle in local coordinates, the part of its texture, and the part its distance field covers (in texture coordinates)
	public struct Placement {
		public Texture texture;
		public Rect rect;
		public Rect uv;
		public Rect area;
	}

	// whether the image can be drawn by a fade
	public static bool CanDraw(Graphic graphic) {
		if (graphic is RawImage) return true;
		return graphic is Image image && (image.type == Image.Type.Simple || (image.type == Image.Type.Sliced && !image.hasBorder));
	}

	// where the image would draw a sprite or texture, false if it can't be drawn (see CanDraw)
	public static bool TryPlace(Graphic graphic, object image, out Placement placement) {
		placement = default;
		Rect rect = graphic.rectTransform.rect;
		if (graphic is RawImage rawImage && image is Texture texture) {
			placement = new Placement { texture = texture, rect = rect, uv = rawImage.uvRect, area = new Rect(0, 0, 1, 1) };
			return true;
		}

		if (!(graphic is Image owner) || !(image is Sprite sprite) || sprite == null || sprite.texture == null) return false;

		// like Image.GetDrawingDimensions: the sprite keeps its transparent padding (which packing removed), and the image can keep its aspect ratio
		Vector4 padding = DataUtility.GetPadding(sprite);
		Vector2 size = sprite.rect.size;
		if (owner.preserveAspect && size.x > 0 && size.y > 0) {
			float spriteRatio = size.x / size.y;
			if (spriteRatio > rect.width / rect.height) {
				float height = rect.width / spriteRatio;
				rect.y += (rect.height - height) * owner.rectTransform.pivot.y;
				rect.height = height;
			}
			else {
				float width = rect.height * spriteRatio;
				rect.x += (rect.width - width) * owner.rectTransform.pivot.x;
				rect.width = width;
			}
		}

		int spriteWidth = Mathf.RoundToInt(size.x);
		int spriteHeight = Mathf.RoundToInt(size.y);
		if (spriteWidth > 0 && spriteHeight > 0) {
			rect = Rect.MinMaxRect(
				rect.x + rect.width * padding.x / spriteWidth,
				rect.y + rect.height * padding.y / spriteHeight,
				rect.x + rect.width * (spriteWidth - padding.z) / spriteWidth,
				rect.y + rect.height * (spriteHeight - padding.w) / spriteHeight);
		}

		Vector4 outer = DataUtility.GetOuterUV(sprite);
		Rect uv = Rect.MinMaxRect(outer.x, outer.y, outer.z, outer.w);
		placement = new Placement { texture = sprite.texture, rect = rect, uv = uv, area = uv };
		return true;
	}

	/**
	 * Shows a point of the fade from one image to the other with the shader. The textures are the distance fields and noise, if the shader uses them.
	 * old can have no texture, if there was no image.
	 */
	public void Show(Shader shader, Placement old, Placement @new, FadeParameters parameters, Texture oldDistance, Texture newDistance, Texture noise) {
		if (!materials.TryGetValue(shader, out Material shaderMaterial)) {
			shaderMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
			materials[shader] = shaderMaterial;
		}

		if (material != shaderMaterial) material = shaderMaterial;
		if (newTexture != @new.texture) {
			newTexture = @new.texture;
			SetMaterialDirty();
		}

		Rect newBounds = old.texture != null ? Rect.MinMaxRect(Mathf.Min(old.rect.xMin, @new.rect.xMin), Mathf.Min(old.rect.yMin, @new.rect.yMin), Mathf.Max(old.rect.xMax, @new.rect.xMax), Mathf.Max(old.rect.yMax, @new.rect.yMax)) : @new.rect;
		if (newBounds != bounds) {
			bounds = newBounds;
			SetVerticesDirty();
		}

		Apply(shaderMaterial, old, @new, parameters, oldDistance, newDistance, noise);
		// the material rendered can be a copy (e.g. with the stencil of a mask), which gets the values too
		Material rendered = canvasRenderer.materialCount > 0 ? canvasRenderer.GetMaterial() : null;
		if (rendered != null && rendered != shaderMaterial) Apply(rendered, old, @new, parameters, oldDistance, newDistance, noise);
	}

	private void Apply(Material target, Placement old, Placement @new, FadeParameters parameters, Texture oldDistance, Texture newDistance, Texture noise) {
		target.SetTexture(OLD_TEX, old.texture != null ? old.texture : Texture2D.blackTexture);
		target.SetVector(OLD_MAP, old.texture != null ? Map(old.rect) : new Vector4(2, 2, 1, 1)); // without an old image, it is nowhere
		target.SetVector(NEW_MAP, Map(@new.rect));
		target.SetVector(OLD_UV, ToVector(old.uv));
		target.SetVector(NEW_UV, ToVector(@new.uv));
		target.SetVector(OLD_AREA, ToVector(old.area));
		target.SetVector(NEW_AREA, ToVector(@new.area));
		target.SetTexture(OLD_DISTANCE, oldDistance != null ? oldDistance : Texture2D.blackTexture);
		target.SetTexture(NEW_DISTANCE, newDistance != null ? newDistance : Texture2D.blackTexture);
		target.SetTexture(NOISE, noise != null ? noise : Texture2D.blackTexture);
		foreach (KeyValuePair<string, float> value in parameters.values) target.SetFloat(GetPropertyId(value.Key), value.Value);
	}

	// where a rectangle is in the bounds (offset and size, 0 to 1)
	private Vector4 Map(Rect rect) {
		if (bounds.width <= 0 || bounds.height <= 0 || rect.width <= 0 || rect.height <= 0) return new Vector4(2, 2, 1, 1);
		return new Vector4((rect.xMin - bounds.xMin) / bounds.width, (rect.yMin - bounds.yMin) / bounds.height, rect.width / bounds.width, rect.height / bounds.height);
	}

	private static Vector4 ToVector(Rect rect) => new Vector4(rect.x, rect.y, rect.width, rect.height);

	// the shader property of a value of FadeParameters: progress is _Progress
	private static int GetPropertyId(string name) {
		if (!PROPERTY_IDS.TryGetValue(name, out int id)) {
			id = Shader.PropertyToID("_" + char.ToUpperInvariant(name[0]) + name.Substring(1));
			PROPERTY_IDS[name] = id;
		}

		return id;
	}

	protected override void OnPopulateMesh(VertexHelper vertices) {
		vertices.Clear();
		if (bounds.width <= 0 || bounds.height <= 0) return;

		Color32 vertexColor = color;
		vertices.AddVert(new Vector3(bounds.xMin, bounds.yMin), vertexColor, new Vector2(0, 0));
		vertices.AddVert(new Vector3(bounds.xMin, bounds.yMax), vertexColor, new Vector2(0, 1));
		vertices.AddVert(new Vector3(bounds.xMax, bounds.yMax), vertexColor, new Vector2(1, 1));
		vertices.AddVert(new Vector3(bounds.xMax, bounds.yMin), vertexColor, new Vector2(1, 0));
		vertices.AddTriangle(0, 1, 2);
		vertices.AddTriangle(2, 3, 0);
	}

	protected override void OnDestroy() {
		base.OnDestroy();
		foreach (Material shaderMaterial in materials.Values) {
			if (Application.isPlaying) Destroy(shaderMaterial);
			else DestroyImmediate(shaderMaterial);
		}
	}
}
}
