# UIConstraints
 Declarative UI Framework for Unity and Godot (C#)

## Repository layout

| Folder | Contents |
|---|---|
| `UIConstraints.Core` | Engine independent parsing, evaluation and code completion of expressions (.NET Standard 2.1, C# 12) |
| `UIConstraints.Core.Tests` | NUnit tests for Core |
| `UIConstraints.Unity` | Unity project. The Unity integration is the embedded package `Packages/uiconstraints`, `Assets/Example` contains a sample scene. |
| `UIConstraints.Godot` | Godot (.NET) project. The Godot integration is the addon in `addons/ui_constraints` (not implemented yet). |

## Working on it

- **Core, tests and Godot**: open `UIConstraints.sln`, or run `dotnet test` in the root folder.
- **Unity**: build Core first (`dotnet build UIConstraints.Core`). Unity uses Core as a precompiled plugin, which every Core build copies to `Packages/uiconstraints/Runtime/Plugins`. Then open `UIConstraints.Unity` with Unity Hub, Unity generates its own solution (`UIConstraints.Unity.sln`) for the Unity code.
- **Godot**: open `UIConstraints.Godot` with the .NET build of Godot 4.7. Godot builds `UIConstraints.Godot.sln`, which compiles Core from source.
