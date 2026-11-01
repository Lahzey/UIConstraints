using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UIConstraints.Core.Animation;

namespace UIConstraints.Core.Tests {
// image fades with a fake engine: what they show when, how they blend, distance fields and noise
public class ImageFadeTests {
	private const float TOLERANCE = 1e-4f;
	private static readonly FadeColor RED = new FadeColor(1, 0, 0, 1);
	private static readonly FadeColor BLUE = new FadeColor(0, 0, 1, 1);

	private FakeEngine engine;

	[SetUp]
	public void SetUp() {
		engine = new FakeEngine();
		ImageFading.backend = engine;
	}

	[TearDown]
	public void TearDown() {
		ImageFading.backend = null;
	}

	private class FakeImage {
		public readonly AlphaImage alpha;

		public FakeImage(AlphaImage alpha = null) {
			this.alpha = alpha;
		}
	}

	private class FakeEngine : IImageFadeBackend, IImageFader {
		public readonly List<(object from, object to, float progress, string shader)> shown = new List<(object, object, float, string)>();
		public int hidden;
		public int reads;

		public bool CanFade(Type valueType) => valueType == typeof(FakeImage);

		public IImageFader CreateFader(object owner) => this;

		public AlphaImage ReadAlpha(object image, int maxSize) {
			reads++;
			return ((FakeImage)image).alpha;
		}

		public void Show(object from, object to, FadeParameters parameters) {
			shown.Add((from, to, parameters.progress, parameters.shader));
		}

		public void Hide() {
			hidden++;
		}
	}

	private static TransitionState State(ImageFadeTransition transition) {
		transition.curve = TransitionCurve.Linear();
		transition.duration = 1;
		return transition.CreateState(new TransitionContext(typeof(FakeImage), new object()));
	}

	private static FadePoint Point(FadeColor oldColor, FadeColor newColor, float oldDistance = 0.5f, float newDistance = 0.5f, float noise = 0.5f) {
		return new FadePoint { oldColor = oldColor, newColor = newColor, oldDistance = oldDistance, newDistance = newDistance, noise = noise };
	}

	private static FadeColor Blend(ImageFadeTransition transition, float progress, FadePoint point) {
		return transition.Blend(point, transition.GetParameters(progress));
	}

	private static void AssertColor(FadeColor actual, FadeColor expected) {
		Assert.That(new[] { actual.r, actual.g, actual.b, actual.a }, Is.EqualTo(new[] { expected.r, expected.g, expected.b, expected.a }).Within(1e-3f));
	}

	[Test]
	public void OnlyImagesCanFade() {
		Assert.That(new CrossfadeTransition().CanAnimate(typeof(FakeImage)), Is.True);
		Assert.That(new EdgeRevealTransition().CanAnimate(typeof(string)), Is.False);
		ImageFading.backend = null;
		Assert.That(new CrossfadeTransition().CanAnimate(typeof(FakeImage)), Is.False); // without an engine
	}

	[Test]
	public void FadesShowWhileMovingAndHideAtTheEnd() {
		FakeImage first = new FakeImage();
		FakeImage second = new FakeImage();
		TransitionState state = State(new EdgeRevealTransition());
		Assert.That(state.Update(first, 0), Is.SameAs(first));
		Assert.That(engine.shown, Is.Empty);

		Assert.That(state.Update(second, 1), Is.SameAs(second)); // the engine object shows the new image, the fader draws the fade over it
		state.Update(second, 1.25);
		Assert.That(engine.shown.Last().from, Is.SameAs(first));
		Assert.That(engine.shown.Last().progress, Is.EqualTo(0.25f).Within(TOLERANCE));
		Assert.That(engine.shown.Last().shader, Is.EqualTo("EdgeReveal"));

		state.Update(second, 2.5);
		Assert.That(engine.hidden, Is.EqualTo(1));
		Assert.That(state.IsMoving, Is.False);
	}

	[Test]
	public void ShowingAnImageRightAwayHidesTheFade() {
		FakeImage first = new FakeImage();
		TransitionState state = State(new CrossfadeTransition());
		state.Update(first, 0);
		state.Update(new FakeImage(), 1);
		state.Update(null, 1.5); // no image, shown right away
		Assert.That(engine.hidden, Is.EqualTo(1));

		state.Update(first, 2);
		state.Update(new FakeImage(), 2.5);
		state.Snap(first); // e.g. changed by the user
		Assert.That(engine.hidden, Is.EqualTo(2));
	}

	[TestCase(0.3, 0)]
	[TestCase(0.7, 1)]
	public void ChangesWhileFadingFadeFromTheMoreVisibleImage(double changeTime, int expectedFrom) {
		FakeImage[] images = { new FakeImage(), new FakeImage(), new FakeImage() };
		TransitionState state = State(new CrossfadeTransition());
		state.Update(images[0], 0);
		state.Update(images[1], 0);
		state.Update(images[1], changeTime);
		state.Update(images[2], changeTime);
		state.Update(images[2], changeTime + 0.1);
		Assert.That(engine.shown.Last().from, Is.SameAs(images[expectedFrom]));
		Assert.That(engine.shown.Last().to, Is.SameAs(images[2]));
		Assert.That(engine.shown.Last().progress, Is.EqualTo(0.1f).Within(0.02f)); // a new fade, which started right away
	}

	[Test]
	public void CrossfadesFadeBothImages() {
		CrossfadeTransition crossfade = new CrossfadeTransition();
		AssertColor(Blend(crossfade, 0, Point(RED, BLUE)), RED);
		AssertColor(Blend(crossfade, 1, Point(RED, BLUE)), BLUE);
		FadeColor halfway = Blend(crossfade, 0.5f, Point(RED, BLUE));
		Assert.That(halfway.a, Is.EqualTo(0.75f).Within(TOLERANCE)); // half transparent over half transparent

		crossfade.oldFadesOutOnTop = true;
		FadeParameters parameters = crossfade.GetParameters(0.5f);
		Assert.That(parameters.oldOnTop, Is.True);
		Assert.That(parameters.newOpacity, Is.EqualTo(1));
		Assert.That(Blend(crossfade, 0.5f, Point(RED, BLUE)).a, Is.EqualTo(1).Within(TOLERANCE));
	}

	[Test]
	public void UnionFadesKeepTheOverlapOpaque() {
		UnionFadeTransition union = new UnionFadeTransition();
		FadeColor overlap = Blend(union, 0.5f, Point(RED, BLUE));
		AssertColor(overlap, new FadeColor(0.5f, 0, 0.5f, 1));

		FadeColor onlyOld = Blend(union, 0.25f, Point(RED, FadeColor.Transparent));
		AssertColor(onlyOld, new FadeColor(1, 0, 0, 0.75f));
		FadeColor onlyNew = Blend(union, 0.25f, Point(FadeColor.Transparent, BLUE));
		AssertColor(onlyNew, new FadeColor(0, 0, 1, 0.25f));
		AssertColor(Blend(union, 1, Point(RED, FadeColor.Transparent)), FadeColor.Transparent);
	}

	[Test]
	public void DissolvesSwitchPointsInTheOrderOfTheNoise() {
		DissolveTransition dissolve = new DissolveTransition { softness = 0.1f };
		AssertColor(Blend(dissolve, 0, Point(RED, BLUE, noise: 0)), RED);
		AssertColor(Blend(dissolve, 1, Point(RED, BLUE, noise: 1)), BLUE);
		AssertColor(Blend(dissolve, 0.5f, Point(RED, BLUE, noise: 0.2f)), BLUE);
		AssertColor(Blend(dissolve, 0.5f, Point(RED, BLUE, noise: 0.8f)), RED);
		Assert.That(Blend(dissolve, 0.5f, Point(RED, BLUE, noise: 0.5f)).b, Is.GreaterThan(0).And.LessThan(1)); // at the soft edge
		Assert.That(dissolve.GetParameters(0.5f).noise, Is.SameAs(NoisePattern.Get(NoiseKind.Clouds, 8, 0)));
	}

	[Test]
	public void EdgeRevealsShrinkTheOldImageAndGrowTheNewOne() {
		EdgeRevealTransition reveal = new EdgeRevealTransition();
		AssertColor(Blend(reveal, 0, Point(RED, BLUE, 0.01f, 1)), RED); // even the edge of the old image, and not even the center of the new one
		AssertColor(Blend(reveal, 1, Point(RED, BLUE, 1, 0.01f)), BLUE);

		// halfway the old image is gone near its edge, the new one is there near its center
		Assert.That(Blend(reveal, 0.5f, Point(RED, FadeColor.Transparent, oldDistance: 0.2f)).a, Is.Zero);
		Assert.That(Blend(reveal, 0.5f, Point(RED, FadeColor.Transparent, oldDistance: 0.8f)).a, Is.EqualTo(1).Within(TOLERANCE));
		Assert.That(Blend(reveal, 0.5f, Point(FadeColor.Transparent, BLUE, newDistance: 0.8f)).a, Is.EqualTo(1).Within(TOLERANCE));
		Assert.That(Blend(reveal, 0.5f, Point(FadeColor.Transparent, BLUE, newDistance: 0.2f)).a, Is.Zero);
		Assert.That(reveal.GetParameters(0.5f).usesDistanceFields, Is.True);
	}

	[Test]
	public void EdgeRevealsWithoutOverlapShrinkFirst() {
		EdgeRevealTransition reveal = new EdgeRevealTransition { overlap = 0 };
		FadeParameters parameters = reveal.GetParameters(0.25f);
		Assert.That(parameters.Get("oldProgress"), Is.EqualTo(0.5f).Within(TOLERANCE));
		Assert.That(parameters.Get("newProgress"), Is.Zero);
		parameters = reveal.GetParameters(0.75f);
		Assert.That(parameters.Get("oldProgress"), Is.EqualTo(1));
		Assert.That(parameters.Get("newProgress"), Is.EqualTo(0.5f).Within(TOLERANCE));
	}

	private static AlphaImage Image(int width, int height, Func<int, int, bool> inside) {
		byte[] alpha = new byte[width * height];
		for (int y = 0; y < height; y++) {
			for (int x = 0; x < width; x++) alpha[y * width + x] = inside(x, y) ? (byte)255 : (byte)0;
		}

		return new AlphaImage(width, height, alpha);
	}

	[Test]
	public void DistanceFieldsAreExact() {
		// an irregular shape: a ring with a notch, compared with the distance to every outside pixel
		AlphaImage image = Image(40, 30, (x, y) => {
			double distance = Math.Sqrt((x - 20) * (x - 20) + (y - 15) * (y - 15));
			return distance < 14 && distance > 4 && !(x > 25 && Math.Abs(y - 15) < 2);
		});
		DistanceField field = DistanceField.Compute(image);

		double[] expected = new double[image.width * image.height];
		for (int y = 0; y < image.height; y++) {
			for (int x = 0; x < image.width; x++) {
				if (image.alpha[y * image.width + x] < 128) continue;
				double nearest = Math.Min(Math.Min(x + 1, image.width - x), Math.Min(y + 1, image.height - y)); // the border counts as outside
				for (int oy = 0; oy < image.height; oy++) {
					for (int ox = 0; ox < image.width; ox++) {
						if (image.alpha[oy * image.width + ox] < 128) nearest = Math.Min(nearest, Math.Sqrt((ox - x) * (ox - x) + (oy - y) * (oy - y)));
					}
				}

				expected[y * image.width + x] = nearest;
			}
		}

		double max = expected.Max();
		for (int i = 0; i < expected.Length; i++) Assert.That(field.values[i], Is.EqualTo(expected[i] / max * 255).Within(0.51), $"pixel {i % image.width}, {i / image.width}");
	}

	[Test]
	public void ShapesShrinkFromTheBorderOfTheImage() {
		DistanceField field = DistanceField.Compute(Image(5, 5, (x, y) => true));
		Assert.That(field.values[2 * 5 + 2], Is.EqualTo(255));
		Assert.That(field.values[0], Is.EqualTo(85)); // 1 of 3
		Assert.That(field.Sample(0.5f, 0.5f), Is.EqualTo(1).Within(TOLERANCE));
	}

	[Test]
	public void EmptyImagesHaveNoDepth() {
		DistanceField field = DistanceField.Compute(Image(4, 3, (x, y) => false));
		Assert.That(field.values, Is.All.EqualTo(0));
	}

	[Test]
	public void DistanceFieldsAreComputedOnce() {
		FakeImage image = new FakeImage(Image(4, 4, (x, y) => true));
		DistanceField field = ImageFading.GetDistanceField(image);
		Assert.That(field, Is.Not.Null);
		Assert.That(ImageFading.GetDistanceField(image), Is.SameAs(field));
		Assert.That(ImageFading.GetDistanceField(new FakeImage()), Is.Null); // can't be read
		Assert.That(engine.reads, Is.EqualTo(2));
	}

	[TestCase(NoiseKind.Pixels)]
	[TestCase(NoiseKind.Smooth)]
	[TestCase(NoiseKind.Clouds)]
	public void NoiseIsSpreadEvenly(NoiseKind kind) {
		byte[] values = NoisePattern.Get(kind, 8, 0).Values;
		Assert.That(values.Length, Is.EqualTo(NoisePattern.SIZE * NoisePattern.SIZE));
		for (int quarter = 0; quarter < 4; quarter++) {
			double share = values.Count(v => v / 64 == quarter) / (double)values.Length;
			Assert.That(share, Is.EqualTo(0.25).Within(0.1), $"quarter {quarter}");
		}
	}

	[TestCase(NoiseKind.Smooth)]
	[TestCase(NoiseKind.Clouds)]
	public void NoiseRepeatsSeamlessly(NoiseKind kind) {
		NoisePattern pattern = NoisePattern.Get(kind, 4, 3);
		for (float v = 0.1f; v < 1; v += 0.2f) {
			// the values at the border are as close to the other side as to their neighbours inside
			float step = 1f / NoisePattern.SIZE;
			float across = Math.Abs(pattern.Sample(step / 2, v) - pattern.Sample(1 - step / 2, v));
			Assert.That(across, Is.LessThan(0.1f));
		}
	}

	[Test]
	public void PixelNoiseHasBlocks() {
		NoisePattern pattern = NoisePattern.Get(NoiseKind.Pixels, 4, 0);
		Assert.That(pattern.Smooth, Is.False);
		Assert.That(pattern.Sample(0.01f, 0.01f), Is.EqualTo(pattern.Sample(0.24f, 0.24f)));
		Assert.That(pattern.Sample(0.01f, 0.01f), Is.Not.EqualTo(pattern.Sample(0.26f, 0.01f)));
	}

	[Test]
	public void SeedsChangeTheNoise() {
		Assert.That(NoisePattern.Get(NoiseKind.Clouds, 8, 1).Values, Is.Not.EqualTo(NoisePattern.Get(NoiseKind.Clouds, 8, 2).Values));
		Assert.That(NoisePattern.Get(NoiseKind.Clouds, 8, 1), Is.SameAs(NoisePattern.Get(NoiseKind.Clouds, 8, 1)));
	}
}
}
