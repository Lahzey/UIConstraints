# UIConstraints
 Declarative UI Framework for Unity and Godot (C#)

## Repository layout

| Folder | Contents |
|---|---|
| `UIConstraints.Core` | Engine independent parsing, evaluation and code completion of expressions (.NET Standard 2.1, C# 12) |
| `UIConstraints.Core.Tests` | NUnit tests for Core |
| `UIConstraints.Unity` | Unity project. The Unity integration is the embedded package `Packages/uiconstraints`, `Assets/Example` contains a sample scene. |
| `UIConstraints.Godot` | Godot (.NET) project. The Godot integration is the addon in `addons/ui_constraints` (not implemented yet). |
| `UIConstraints.Icons` | The component icons as SVGs, shared by the engines, and the converter that renders them to PNGs for Unity (see its README). |

## Working on it

- **Core, tests and Godot**: open `UIConstraints.sln`, or run `dotnet test` in the root folder.
- **Unity**: build Core first (`dotnet build UIConstraints.Core`). Unity uses Core as a precompiled plugin, which every Core build copies to `Packages/uiconstraints/Runtime/Plugins`. Then open `UIConstraints.Unity` with Unity Hub, Unity generates its own solution (`UIConstraints.Unity.sln`) for the Unity code.
- **Godot**: open `UIConstraints.Godot` with the .NET build of Godot 4.7. Godot builds `UIConstraints.Godot.sln`, which compiles Core from source.

## Expressions and actions

Constraints set properties of UI components to the values of C#-like expressions, e.g. `player.HealthPercent` for the fill amount of an image, and are rendered every frame.
Actions are inputs that run when something happens, e.g. the `onClick` of a Button Constraint: method calls and assignments, separated by `;` (`shop.Buy(item, 1); GameLog.Add("Bought " + item)`).
Events with a value pass it as the variable `value` (e.g. `settings.volume = value` for a slider), so inputs show the state and their actions change it.

Besides member access, method calls, casts, generics and the usual operators, expressions support:
- `new` (`new Vector2(1, 2)`, `new int[3]`, `new int[2, 3]`) and collections (`["a", "b"]`, an array of the type all elements convert to)
- indexers (`list[0]`, `dictionary[key]`, `grid[x, y]`), negation and the operators of types (`Vector2.one * 2`)
- generic methods, with type arguments (`GetComponent<Image>()`) or inferred ones (`Enumerable.Max(numbers)`)
- nullable types (`int?`), multidimensional array types (`int[,]`), and all numeric types with their literal suffixes (`5u`, `5ul`, `1.5m`)
Assignments (`=`, `+=`, `-=`, `*=`, `/=`) and multiple statements are only allowed in actions, as everything else should not change anything.

## Animation

Every value input can animate its changes instead of showing them right away: click the curve button in the title of the input. Collapsed, the input shows a summary of the animation (e.g. "Interpolate · 0.3 s"); expanded, its settings. Animating a `Variable` animates the value for everything that uses it.

| Transition | For | What it does |
|---|---|---|
| Interpolate | numbers, vectors, colors, rects, and any type with `+`, `-` and `*` by a number | moves along a curve |
| Spring | the same | springs to the new value, may overshoot, keeps its momentum when the value changes again |
| Numbers in Text | text | counts the numbers in a text (`"50 gold"` to `"42 gold"`), keeping the rest |
| Scramble | text | random characters settle into the new text |
| Typewriter | text | deletes the old text and types the new one |
| Crossfade | sprites and textures | fades the new image in while the old one fades out (or, for opaque images, the old one out over the new one) |
| Union Fade | sprites and textures | like a crossfade, but where both images are opaque it stays opaque, for icons with transparent parts |
| Edge Reveal | sprites and textures | shrinks the old image from its edges while the new one grows from its center, following their shapes |
| Dissolve | sprites and textures | switches point by point in the order of a noise pattern (pixels, smooth or clouds) |
| Switch | anything | changes after the delay |

All transitions have a delay (a change while waiting restarts it, e.g. for a damage bar that catches up once no more damage is taken), can be limited to changes in one direction (e.g. only when health decreases), run in real or scaled time, and can animate from an expression the first time a value is shown (e.g. `0` for a counter that counts up when the UI appears).
Timed transitions take a duration and curve, and optionally a distance that takes the whole duration, so small changes are quicker on a logarithmic scale: a counter from 50 to 51 is almost instant, but 1 to 1,000,000 doesn't take forever.

Transitions live in Core (`UIConstraints.Core.Animation`), with their own curve type (`TransitionCurve`, converted from Unity's `AnimationCurve` by `CurveConversion`). Custom transitions extend `Transition` (or `TimedTransition`) with public fields as their settings and appear in the editor automatically; types can be made blendable with `ValueBlending.Register`.

Image fades (`ImageFadeTransition`) are mostly in Core too: it decides how a fade looks at each point (`GetParameters`), and computes it per pixel as a reference (`Blend`), which the engine's shaders match (tests compare them pixel by pixel). It also computes the distance fields of images for Edge Reveal (`DistanceField`, from the alpha, at most 128 px, once per image) and the noise patterns for Dissolve (`NoisePattern`), so they look the same in every engine. The engine registers an `IImageFadeBackend` that draws fades and reads the alpha of images.
In Unity, the image is hidden during a fade and a hidden child draws it instead (`FadeGraphic`, with the shader `UIConstraints/Fade/{key}`). Union Fade, Edge Reveal and Dissolve need simple images or raw images, other image types (e.g. sliced) crossfade instead, which the inspector warns about. A custom fade extends `ImageFadeTransition` and needs a shader with its key in each engine, in Unity in a Resources folder (or always included), see `UIConstraintsFade.cginc`.
