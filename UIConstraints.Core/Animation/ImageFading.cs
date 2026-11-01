using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace UIConstraints.Core.Animation;

// a color with straight (not premultiplied) alpha, each 0 to 1
public struct FadeColor {
    public float r, g, b, a;

    public FadeColor(float r, float g, float b, float a) {
        this.r = r;
        this.g = g;
        this.b = b;
        this.a = a;
    }

    public static FadeColor Transparent => new FadeColor(0, 0, 0, 0);

    // the colors mixed by their weights (which already include their alpha), with the given alpha
    public static FadeColor Mix(FadeColor first, float firstWeight, FadeColor second, float secondWeight, float alpha) {
        float total = firstWeight + secondWeight;
        if (total <= 0 || alpha <= 0) return Transparent;
        return new FadeColor(
            (first.r * firstWeight + second.r * secondWeight) / total,
            (first.g * firstWeight + second.g * secondWeight) / total,
            (first.b * firstWeight + second.b * secondWeight) / total,
            alpha);
    }

    // the top color with its opacity drawn over the bottom one with its opacity (normal alpha blending)
    public static FadeColor Over(FadeColor top, float topOpacity, FadeColor bottom, float bottomOpacity) {
        float topAlpha = top.a * topOpacity;
        float bottomAlpha = bottom.a * bottomOpacity * (1 - topAlpha);
        return Mix(top, topAlpha, bottom, bottomAlpha, topAlpha + bottomAlpha);
    }
}

// what the shader of a fade sees at one point: both images, how deep the point is inside each of their shapes (see DistanceField) and the noise pattern
public struct FadePoint {
    public FadeColor oldColor;
    public FadeColor newColor;
    public float oldDistance;
    public float newDistance;
    public float noise;
}

/**
 * How a fade looks at one point of its progress. Engines draw the old and new image on top of each other with these opacities, or, if there is a shader
 * (and the engine can use it), draw both with the shader, which gets the values. Each engine has its own shader for each key (e.g. UIConstraints/Fade/EdgeReveal in Unity),
 * ImageFadeTransition.Blend is what they compute.
 */
public class FadeParameters {
    public float progress; // as curved, 0 to 1
    public float oldOpacity;
    public float newOpacity;
    public bool oldOnTop; // whether the old image is drawn over the new one instead of below it

    [CanBeNull] public string shader;
    public bool usesDistanceFields; // whether the shader needs the distance fields of the images (see DistanceField)
    [CanBeNull] public NoisePattern noise; // the pattern the shader needs, if any

    // the values the shader gets (by name, engines adjust them to their naming, e.g. progress is _Progress in Unity)
    public readonly Dictionary<string, float> values = new Dictionary<string, float>();

    public float Get(string name) => values.TryGetValue(name, out float value) ? value : 0;
}

// the alpha of an image (0 to 255) row by row, see IImageFadeBackend.ReadAlpha
public class AlphaImage {
    public readonly int width;
    public readonly int height;
    public readonly byte[] alpha;

    public AlphaImage(int width, int height, byte[] alpha) {
        if (alpha.Length != width * height) throw new ArgumentException($"{width}x{height} pixels need {width * height} values, not {alpha.Length}");
        this.width = width;
        this.height = height;
        this.alpha = alpha;
    }
}

/**
 * What an engine provides to fade images: it draws the fades and reads the pixels of images (for distance fields). Engines register theirs as ImageFading.backend.
 */
public interface IImageFadeBackend {
    // whether values of the type are images it can fade (e.g. sprites and textures)
    bool CanFade(Type valueType);

    // draws the fades of the images shown on an engine object (e.g. an image component), null if it can't
    [CanBeNull]
    IImageFader CreateFader([CanBeNull] object owner);

    /**
     * The alpha of an image, scaled down to at most maxSize pixels on its longer side (keeping its aspect ratio), null if it can't be read.
     * The rows can be in either order, as long as the engine uses the same order for the textures it makes from them (e.g. of distance fields).
     */
    [CanBeNull]
    AlphaImage ReadAlpha(object image, int maxSize);
}

public interface IImageFader {
    // shows the fade from one image to another at a point (the engine object itself shows the new one already)
    void Show(object from, object to, FadeParameters parameters);

    // stops showing the fade, as it finished or stopped, so the engine object shows its image normally again
    void Hide();
}

// the image fading of the engine, and what fades share between images
public static class ImageFading {
    [CanBeNull] public static IImageFadeBackend backend;

    private static readonly ConditionalWeakTable<object, DistanceFieldHolder> DISTANCE_FIELDS = new ConditionalWeakTable<object, DistanceFieldHolder>();

    // the distance field of an image (computed once, while the image is in use), null if the backend can't read it
    [CanBeNull]
    public static DistanceField GetDistanceField(object image) {
        if (DISTANCE_FIELDS.TryGetValue(image, out DistanceFieldHolder holder)) return holder.field;

        AlphaImage alpha = backend?.ReadAlpha(image, DistanceField.MAX_SIZE);
        holder = new DistanceFieldHolder { field = alpha == null ? null : DistanceField.Compute(alpha) };
        DISTANCE_FIELDS.AddOrUpdate(image, holder);
        return holder.field;
    }

    private class DistanceFieldHolder {
        [CanBeNull] public DistanceField field;
    }
}

/**
 * Fades from one image to another (e.g. the sprite of an image) instead of switching right away. The engine draws the fade (see IImageFadeBackend), this decides how it looks.
 * To add one, extend this: set the opacities, or a shader with its values in GetParameters, and override Blend with what the shader computes.
 * Each engine needs the shader then, Unity finds it by name: UIConstraints/Fade/{key}.
 * Changes while fading fade from the image that is more visible at that moment.
 */
[Serializable]
public abstract class ImageFadeTransition : TimedTransition {
    protected ImageFadeTransition() {
        duration = 0.4f;
        curve = TransitionCurve.EaseInOut();
    }

    public override bool CanAnimate(Type valueType) => ImageFading.backend?.CanFade(valueType) ?? false;

    public override TransitionState CreateState(TransitionContext context) => new State(this, context);

    /**
     * How the fade looks at a point of its progress (as curved, 0 to 1). The parameters are a crossfade when this is called, with progress as a value,
     * which engines also draw if they can't use the shader (e.g. for sliced images in Unity).
     */
    public virtual void GetParameters(float progress, FadeParameters parameters) { }

    // the color the shader computes at a point, as the reference for the shaders of the engines. Without a shader the images are drawn on top of each other
    public virtual FadeColor Blend(FadePoint point, FadeParameters parameters) {
        return parameters.oldOnTop
            ? FadeColor.Over(point.oldColor, parameters.oldOpacity, point.newColor, parameters.newOpacity)
            : FadeColor.Over(point.newColor, parameters.newOpacity, point.oldColor, parameters.oldOpacity);
    }

    // the parameters at a point of the progress (as curved)
    public FadeParameters GetParameters(float progress) {
        FadeParameters parameters = new FadeParameters();
        SetParameters(progress, parameters);
        return parameters;
    }

    private void SetParameters(float progress, FadeParameters parameters) {
        parameters.progress = progress;
        parameters.oldOpacity = 1 - progress;
        parameters.newOpacity = progress;
        parameters.oldOnTop = false;
        parameters.shader = null;
        parameters.usesDistanceFields = false;
        parameters.noise = null;
        parameters.values.Clear();
        parameters.values["progress"] = progress;
        GetParameters(progress, parameters);
    }

    protected static float Clamp01(float value) => value < 0 ? 0 : value > 1 ? 1 : value;

    // 0 below the edge, 1 above it, smooth in between
    protected static float SmoothStep(float edge0, float edge1, float value) {
        float t = edge1 > edge0 ? Clamp01((value - edge0) / (edge1 - edge0)) : value < edge0 ? 0 : 1;
        return t * t * (3 - 2 * t);
    }

    private class State : TimedTransitionState<ImageFadeTransition> {
        [CanBeNull] private readonly IImageFader fader;
        private readonly FadeParameters parameters = new FadeParameters();
        private object from;
        private object to;
        private bool showing;
        private float lastCurved;

        public State(ImageFadeTransition transition, TransitionContext context) : base(transition, context) {
            fader = ImageFading.backend?.CreateFader(context.owner);
        }

        protected override void BeginMoving(object from, object to) {
            if (showing && lastCurved < 0.5f) from = this.from; // changed again while fading: from the image that is more visible
            this.from = from;
            this.to = to;
        }

        // a fade can't move to a different image halfway, it fades again (see BeginMoving)
        protected override bool Retarget(object from, object to) => false;

        protected override object Sample(float curved, float progress, double elapsed) {
            if (fader == null || from == null || ReferenceEquals(from, to)) return to;

            if (progress >= 1) {
                HideFade();
            }
            else {
                transition.SetParameters(Clamp01(curved), parameters);
                fader.Show(from, to, parameters);
                showing = true;
                lastCurved = curved;
            }

            return to;
        }

        protected override void Stopped() => HideFade();

        private void HideFade() {
            if (!showing) return;
            showing = false;
            fader?.Hide();
        }
    }
}
