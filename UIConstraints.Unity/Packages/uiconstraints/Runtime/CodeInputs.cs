using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using UIConstraints.Core.Parsing;
using UnityEngine;

namespace UIConstraints.Unity {
// the type the value of a CodeInput field has to be convertible to (e.g. bool for a condition), checked while parsing so mistakes show in the editor
[AttributeUsage(AttributeTargets.Field)]
public class ResultTypeAttribute : Attribute {
	public readonly Type type;

	public ResultTypeAttribute(Type type) {
		this.type = type;
	}
}

// marks a CodeInput field that contains a type (e.g. List<int>) instead of a value
[AttributeUsage(AttributeTargets.Field)]
public class TypeInputAttribute : Attribute { }

/**
 * Marks a CodeInput field that contains an action: statements that are executed when something happens (e.g. a button is clicked), see ParseTarget.Action.
 * Events with a value (e.g. the new value of a slider) provide it to the action as a variable.
 */
[AttributeUsage(AttributeTargets.Field)]
public class ActionAttribute : Attribute {
	[CanBeNull] public readonly string parameterName;
	[CanBeNull] public readonly Type parameterType;

	public ActionAttribute() { }

	public ActionAttribute(string parameterName, Type parameterType) {
		this.parameterName = parameterName;
		this.parameterType = parameterType;
	}
}

/**
 * A component whose CodeInput fields share their usings, as they are mostly the same, which the inspector then shows once instead of for every input.
 * An input still uses its own usings too (e.g. those it had before its component shared them).
 */
public interface ISharedUsings {
	IReadOnlyList<string> SharedUsings { get; }
}

/**
 * Parses the CodeInput fields of components with everything their result depends on: the usings of the input, the variables available at the component (see VariableScopes)
 * and what the field expects (see ResultTypeAttribute and TypeInputAttribute). Constraints, the inspector and the editor validation all parse this way, so they always agree.
 */
public static class CodeInputs {
	private static readonly Dictionary<Type, List<FieldInfo>> FIELDS_BY_TYPE = new Dictionary<Type, List<FieldInfo>>();

	// the serialized CodeInput fields of a component type (public ones and those with [SerializeField], including inherited ones)
	public static IReadOnlyList<FieldInfo> GetFields(Type componentType) {
		lock (FIELDS_BY_TYPE) {
			if (FIELDS_BY_TYPE.TryGetValue(componentType, out List<FieldInfo> cachedFields)) return cachedFields;

			List<FieldInfo> fields = new List<FieldInfo>();
			for (Type type = componentType; type != null && type != typeof(MonoBehaviour); type = type.BaseType)
				foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
					if (field.FieldType == typeof(CodeInput) && (field.IsPublic || field.IsDefined(typeof(SerializeField), false)))
						fields.Add(field);

			FIELDS_BY_TYPE[componentType] = fields;
			return fields;
		}
	}

	public static FieldInfo GetField(Type componentType, string fieldName) {
		return GetFields(componentType).First(f => f.Name == fieldName);
	}

	public static ParseTarget GetTarget(FieldInfo field) {
		if (field.IsDefined(typeof(TypeInputAttribute), false)) return ParseTarget.Type;
		return field.IsDefined(typeof(ActionAttribute), false) ? ParseTarget.Action : ParseTarget.Value;
	}

	// the variables an input of the field can use: those available at the component, plus the parameter of actions (which hides a variable with the same name)
	public static IReadOnlyDictionary<string, Type> GetVariables(FieldInfo field, IReadOnlyDictionary<string, Type> availableVariables) {
		ActionAttribute action = field.GetCustomAttribute<ActionAttribute>();
		if (action?.parameterName == null) return availableVariables;

		Dictionary<string, Type> variables = availableVariables.ToDictionary(v => v.Key, v => v.Value);
		variables[action.parameterName] = action.parameterType;
		return variables;
	}

	[CanBeNull]
	public static Type GetResultType(FieldInfo field) {
		return field.GetCustomAttribute<ResultTypeAttribute>()?.type;
	}

	// the usings an input of the component is parsed with: those it shares with the other inputs of the component (see ISharedUsings), and its own
	public static IEnumerable<string> GetUsings(Component owner, [CanBeNull] CodeInput input) {
		IEnumerable<string> shared = (owner as ISharedUsings)?.SharedUsings ?? Enumerable.Empty<string>();
		return input == null ? shared : shared.Concat(input.usings);
	}

	// parses a CodeInput field of a component, blocks until it is parsed (see ParseCache.GetParseResultAwait)
	public static ParseResult Parse(Component owner, FieldInfo field) {
		return Parse(owner, field, VariableScopes.GetAvailableVariables(owner));
	}

	// like Parse, for when the variables available at the component are already known
	public static ParseResult Parse(Component owner, FieldInfo field, IReadOnlyDictionary<string, Type> availableVariables) {
		CodeInput input = (CodeInput)field.GetValue(owner);
		ParseContext context = new ParseContext(GetUsings(owner, input), GetVariables(field, availableVariables));
		return ParseCache.GetParseResultAwait(input?.input ?? "", context, GetTarget(field), GetResultType(field));
	}
}
}
