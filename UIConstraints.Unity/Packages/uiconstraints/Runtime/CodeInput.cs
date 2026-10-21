using System;
using System.Collections.Generic;
using UIConstraints.Core.Parsing;
using UnityEngine.Serialization;

namespace UIConstraints.Unity {
[Serializable]
public class CodeInput {
	[FormerlySerializedAs("Input")] public string input = "";
	public List<string> usings = new List<string>(); // namespaces whose types can be used by just their name, like using directives in C#

	// blocks until parsed, do not use from UI (see ParseCache.GetParseResultAwait)
	public ParseResult Result => ParseCache.GetParseResultAwait(input, new ParseContext(usings));
}
}
