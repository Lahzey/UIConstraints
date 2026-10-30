using System;
using System.Collections.Generic;
using UnityEngine.Serialization;

namespace UIConstraints.Unity {
// an expression (or type) entered in the inspector, parsed with CodeInputs, as the result depends on where it is used
[Serializable]
public class CodeInput {
	[FormerlySerializedAs("Input")] public string input = "";
	public List<string> usings = new List<string>(); // namespaces whose types can be used by just their name, like using directives in C#. Components can share them instead (see ISharedUsings)
}
}
