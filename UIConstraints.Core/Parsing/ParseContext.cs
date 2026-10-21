using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace UIConstraints.Core.Parsing {
/**
 * Information about the environment code is parsed in, which is needed to resolve names, e.g. the namespaces types can be referenced from by just their name (like using directives in C#).
 */
public class ParseContext {
	public static readonly ParseContext EMPTY = new ParseContext(null);

	public readonly IReadOnlyList<string> usings;
	public readonly string key; // identical for contexts that resolve names the same way, used to cache parse results

	public ParseContext([CanBeNull] IEnumerable<string> usings) {
		this.usings = usings?.Select(u => u?.Trim()).Where(u => !string.IsNullOrEmpty(u)).Distinct().ToList() ?? new List<string>();
		key = string.Join(";", this.usings.OrderBy(u => u, StringComparer.Ordinal));
	}

	// resolves a type name the way C# would with the usings of this context, returns null if no type or more than one type (ambiguous) was found
	[CanBeNull]
	public Type ResolveType(string name) {
		return TypeResolver.Resolve(name, usings);
	}
}
}
