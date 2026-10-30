# UI Constraints icons

The icons of the UI Constraints components, shared by the engine integrations. The SVGs in `svg/` are the source, every engine uses them from there:

- **Godot** uses the SVGs directly (e.g. `@icon("res://addons/uiconstraints/icons/slider.svg")`, or `[Icon]` in C#), it renders them crisply at any editor scale.
- **Unity** can't use SVGs as component icons, so `Converter/` renders them to PNGs in `UIConstraints.Unity/Packages/uiconstraints/Editor/Icons/`, at 16px and 32px (`@2x`, for high resolution displays). The components reference them with `[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/<name>.png")]`.

After changing or adding an SVG, run the converter from the repository root (it only rewrites PNGs that changed):

```bash
dotnet run --project UIConstraints.Icons/Converter
```

`dotnet run --project UIConstraints.Icons/Converter -- --sheet icons-sheet.png` renders all icons enlarged on the dark and light inspector backgrounds of Unity instead, to check how they look on both.

## Conventions

Each SVG starts with a comment saying what it shows and which components use it, and every shape is commented.

- **16x16 grid** (`viewBox="0 0 16 16"`), the size of component icons in both engines. 1px strokes lie on half pixel coordinates (e.g. `x="1.5"`), so they cover whole pixels instead of blurring across two.
- **The color says the category**, as fill and darker outline:

  | Category | Fill | Outline | Icons |
  |---|---|---|---|
  | Visual: what an element looks like | `#4C9BE8` | `#1F5F9E` | text, image, raw-image, shadow, canvas-group |
  | Layout: where an element is and how big | `#5DBB63` | `#2E7D32` | rect-transform, layout-element, layout-group, grid-layout-group |
  | Input: elements that run actions | `#A77BDB` | `#5E3A8C` | button, toggle, slider, scrollbar, dropdown, input-field, scroll-rect, pointer-events |
  | Logic: variables, repetition, visibility | `#3FB8AF` | `#1D6F69` | show, for-each, variable, expected-variable, render-manager |

  White (`#FFFFFF`) and gray (`#9E9E9E`, `#8A939B`) are used for details such as labels, tracks and shadows. Details that stand alone use the fill color rather than the outline, which is hard to see on the dark Unity skin.
- **The amber badge** (`#F2B134` with `#8A5A00` outline, and an `=` in `#4A3000`) in the bottom right (x and y from 9.5 to 15.5) marks every UI Constraints component. It tells them apart from the built-in component they control, e.g. Image Constraint from Image. The main shape avoids that corner or is partly covered by it.
- File names are lowercase with dashes and name the concept, not an engine class (e.g. `slider.svg`, not `SliderConstraint.svg`), so the same icon fits the components of every engine.
