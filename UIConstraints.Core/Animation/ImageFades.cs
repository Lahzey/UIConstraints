using System;
using System.ComponentModel;

namespace UIConstraints.Core.Animation;

[Serializable]
[DisplayName("Crossfade")]
[Description("Fades the new image in while the old one fades out. Halfway both are half transparent, so they let a little of what is behind them through (except with 'Old Fades Out On Top'). For sprites and textures.")]
public class CrossfadeTransition : ImageFadeTransition {
    [Description("Shows the new image right away, below the old one fading out. Looks best for images without transparency, as transparent parts of the old image show the new one right away.")]
    public bool oldFadesOutOnTop;

    public override void GetParameters(float progress, FadeParameters parameters) {
        if (!oldFadesOutOnTop) return;
        parameters.newOpacity = 1;
        parameters.oldOnTop = true;
    }
}

[Serializable]
[DisplayName("Union Fade")]
[Description("Where both images are opaque, it stays opaque while the colors change. The rest fades in and out. For icons with transparent parts. For sprites and textures.")]
public class UnionFadeTransition : ImageFadeTransition {
    public override void GetParameters(float progress, FadeParameters parameters) {
        parameters.shader = "UnionFade";
    }

    public override FadeColor Blend(FadePoint point, FadeParameters parameters) {
        float progress = parameters.Get("progress");
        float oldWeight = point.oldColor.a * (1 - progress);
        float newWeight = point.newColor.a * progress;
        float alpha = Math.Max(Math.Max(oldWeight, newWeight), Math.Min(point.oldColor.a, point.newColor.a));
        return FadeColor.Mix(point.oldColor, oldWeight, point.newColor, newWeight, alpha);
    }
}

[Serializable]
[DisplayName("Dissolve")]
[Description("Switches from the old image to the new one point by point, in the order of a noise pattern. For sprites and textures.")]
public class DissolveTransition : ImageFadeTransition {
    [Description("The noise pattern deciding which points switch first.")]
    public NoiseKind pattern = NoiseKind.Clouds;

    [Description("How many cells of the pattern fit across the image (e.g. how many blocks of pixels).")]
    public int scale = 8;

    [Description("How soft the edges between the images are, 0 for hard edges.")]
    public float softness = 0.1f;

    [Description("Changes the pattern, for different patterns on images next to each other.")]
    public int seed;

    public override void GetParameters(float progress, FadeParameters parameters) {
        parameters.shader = "Dissolve";
        parameters.noise = NoisePattern.Get(pattern, scale, seed);
        parameters.values["softness"] = Math.Max(softness, 0.001f);
    }

    public override FadeColor Blend(FadePoint point, FadeParameters parameters) {
        float progress = parameters.Get("progress");
        float softness = parameters.Get("softness");
        float switched = Clamp01((progress * (1 + softness) - point.noise) / softness);
        float oldWeight = point.oldColor.a * (1 - switched);
        float newWeight = point.newColor.a * switched;
        return FadeColor.Mix(point.oldColor, oldWeight, point.newColor, newWeight, oldWeight + newWeight);
    }
}

[Serializable]
[DisplayName("Edge Reveal")]
[Description("Shrinks the old image from its edges inwards while the new one grows from its center outwards, following their shapes. For icons with transparent parts. For sprites and textures.")]
public class EdgeRevealTransition : ImageFadeTransition {
    [Description("How soft the growing and shrinking edges are, as a share of the depth of the image.")]
    public float softness = 0.03f;

    [Description("How much the old image shrinking and the new one growing happen at the same time: 1 at once, 0 one after the other.")]
    public float overlap = 1;

    public override void GetParameters(float progress, FadeParameters parameters) {
        // each image takes this share of the fade
        float share = (1 + Clamp01(overlap)) / 2;
        parameters.shader = "EdgeReveal";
        parameters.usesDistanceFields = true;
        parameters.values["oldProgress"] = Clamp01(progress / share);
        parameters.values["newProgress"] = Clamp01((progress - (1 - share)) / share);
        parameters.values["softness"] = Math.Max(softness, 0.001f);
    }

    public override FadeColor Blend(FadePoint point, FadeParameters parameters) {
        float softness = parameters.Get("softness");
        // the depth from which on the images are visible: rises through the old image, falls through the new one
        float oldThreshold = parameters.Get("oldProgress") * (1 + 2 * softness) - softness;
        float newThreshold = (1 - parameters.Get("newProgress")) * (1 + 2 * softness) - softness;
        float oldWeight = point.oldColor.a * SmoothStep(oldThreshold - softness, oldThreshold + softness, point.oldDistance);
        float newWeight = point.newColor.a * SmoothStep(newThreshold - softness, newThreshold + softness, point.newDistance);
        return FadeColor.Mix(point.oldColor, oldWeight, point.newColor, newWeight, Math.Max(oldWeight, newWeight));
    }
}
