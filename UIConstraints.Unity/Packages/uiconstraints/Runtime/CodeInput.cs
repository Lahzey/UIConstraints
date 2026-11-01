using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UIConstraints.Core.Animation;
using UnityEngine;
using UnityEngine.Serialization;

namespace UIConstraints.Unity {
// an expression (or type) entered in the inspector, parsed with CodeInputs, as the result depends on where it is used
[Serializable]
public class CodeInput {
	[FormerlySerializedAs("Input")] public string input = "";
	public List<string> usings = new List<string>(); // namespaces whose types can be used by just their name, like using directives in C#. Components can share them instead (see ISharedUsings)

	// how the value moves to a new value instead of changing right away, null if it changes right away. Only for inputs that can be animated (see IAnimatedInputs)
	[SerializeReference, CanBeNull] public Transition transition;
}
}
