using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;
using UIConstraints.Unity.Constraints;
using UnityEngine;

namespace UIConstraints.Unity.Tests {
// static data the expressions in the tests use
public static class TestData {
	public static List<int> numbers = new List<int> { 1, 2, 3 };
	public static ArrayList untypedNumbers = new ArrayList { 1, 2 };
	public static List<int> changingNumbers = new List<int>(); // for tests that change it, which set it up themselves
	public static int clicked;
	public static float volume;
}

public static class TestHelpers {
	public const string TEST_NAMESPACE = "UIConstraints.Unity.Tests";

	// sets a private serialized field, like the inspector would (SerializedObject is not available in play mode tests)
	public static void SetField(Component component, string fieldName, object value) {
		for (System.Type type = component.GetType(); type != null; type = type.BaseType) {
			FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
			if (field == null) continue;
			field.SetValue(component, value);
			return;
		}

		throw new System.ArgumentException($"{component.GetType().Name} has no field {fieldName}");
	}

	public static CodeInput Input(string input) {
		return new CodeInput { input = input, usings = new List<string> { TEST_NAMESPACE } };
	}

	public static T AddVariable<T>(GameObject gameObject, string name, string inputField, string input) where T : Component {
		T component = gameObject.AddComponent<T>();
		SetField(component, "variableName", name);
		SetField(component, inputField, Input(input));
		return component;
	}

	// adds a constraint and sets its CodeInput fields (pairs of field name and input)
	public static T AddConstraint<T>(GameObject gameObject, params string[] fieldsAndInputs) where T : Component {
		T component = gameObject.AddComponent<T>();
		for (int i = 0; i < fieldsAndInputs.Length; i += 2) SetField(component, fieldsAndInputs[i], Input(fieldsAndInputs[i + 1]));
		return component;
	}

	public static RecordingConstraint AddRecording(GameObject gameObject, string input) {
		RecordingConstraint recording = gameObject.AddComponent<RecordingConstraint>();
		SetField(recording, "value", Input(input));
		return recording;
	}

	public static GameObject CreateChild(string name, GameObject parent) {
		GameObject child = new GameObject(name);
		child.transform.SetParent(parent.transform);
		return child;
	}
}
}
