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
