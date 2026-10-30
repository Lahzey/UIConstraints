using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace UIConstraints.Core.Parsing;

/**
 * Information about the environment code is parsed in, which is needed to resolve names, e.g. the namespaces types can be referenced from by just their name (like using directives in C#).
 */
public class ParseContext {
    public static readonly ParseContext EMPTY = new ParseContext(null);

    public readonly IReadOnlyList<string> usings; // as entered: namespaces, or type aliases in the form Alias=Full.Type.Name (like using Alias = Full.Type.Name; in C#)
    public readonly IReadOnlyList<string> namespaces;
    public readonly IReadOnlyDictionary<string, string> aliases; // <alias, fully qualified type name in C# syntax>
    public readonly IReadOnlyDictionary<string, Type> variables; // <name, type> of the variables that will be set in the DataContext when evaluating (e.g. by a ForEachConstraint)
    public readonly string key; // identical for contexts that resolve names the same way, used to cache parse results

    public ParseContext([CanBeNull] IEnumerable<string> usings, [CanBeNull] IEnumerable<KeyValuePair<string, Type>> variables = null) {
        this.usings = usings?.Select(u => u?.Trim()).Where(u => !string.IsNullOrEmpty(u)).Distinct().ToList() ?? new List<string>();

        List<string> namespaces = new List<string>();
        Dictionary<string, string> aliases = new Dictionary<string, string>();
        foreach (string entry in this.usings) {
            int equalsIndex = entry.IndexOf('=');
            if (equalsIndex < 0) namespaces.Add(entry);
            else aliases[entry.Substring(0, equalsIndex).Trim()] = entry.Substring(equalsIndex + 1).Trim();
        }

        this.namespaces = namespaces;
        this.aliases = aliases;

        Dictionary<string, Type> variableTypes = new Dictionary<string, Type>();
        if (variables != null)
            foreach (KeyValuePair<string, Type> variable in variables)
                variableTypes[variable.Key] = variable.Value ?? throw new ArgumentException($"The type of the variable '{variable.Key}' is null");
        this.variables = variableTypes;

        key = string.Join(";", this.usings.OrderBy(u => u, StringComparer.Ordinal)) + "|" + string.Join(";", variableTypes.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => v.Key + ":" + v.Value.AssemblyQualifiedName));
    }

    // resolves a (non-nested) type name the way C# would with the usings of this context, returns null if no type or more than one type (ambiguous) was found
    [CanBeNull]
    public Type ResolveType(string metadataName) {
        if (aliases.TryGetValue(metadataName, out string target)) return TypeResolver.ResolveFullyQualifiedType(target);
        return TypeResolver.Resolve(metadataName, namespaces);
    }
}