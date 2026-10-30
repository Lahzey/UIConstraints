using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UIConstraints.Core.Parsing;
using UIConstraints.Unity.Constraints;
using UIConstraints.Unity.Inspector;
using UnityEngine;
using static UIConstraints.Unity.Tests.TestHelpers;
using Object = UnityEngine.Object;

namespace UIConstraints.Unity.Tests {
// which variables components can use (in edit mode, components are not awoken, so this works purely on the hierarchy like the editor does)
public class VariableScopeTests {
	private GameObject root;

	[SetUp]
	public void SetUp() {
		root = new GameObject("root");
	}

	[TearDown]
	public void TearDown() {
		Object.DestroyImmediate(root);
	}

	private static Dictionary<string, string> Describe(Dictionary<string, Type> variables) {
		return variables.ToDictionary(v => v.Key, v => TypeNames.GetDisplayName(v.Value));
	}

	[Test]
	public void ParentsAndPreviousComponentsProvideVariables() {
		Variable numbers = AddVariable<Variable>(root, "numbers", "value", "TestData.numbers");
		Variable count = AddVariable<Variable>(root, "count", "value", "numbers.Count");
		GameObject list = CreateChild("list", root);
		ForEachConstraint forEach = AddVariable<ForEachConstraint>(list, "item", "collection", "numbers");
		GameObject template = CreateChild("template", list);
		RecordingConstraint recording = AddRecording(template, "item + count");

		Assert.That(Describe(VariableScopes.GetAvailableVariables(numbers)), Is.Empty);
		Assert.That(Describe(VariableScopes.GetAvailableVariables(count)), Is.EqualTo(new Dictionary<string, string> { { "numbers", "List<int>" } }));
		Assert.That(Describe(VariableScopes.GetAvailableVariables(forEach)), Is.EqualTo(new Dictionary<string, string> { { "numbers", "List<int>" }, { "count", "int" } }));
		Assert.That(Describe(VariableScopes.GetAvailableVariables(recording)), Is.EqualTo(new Dictionary<string, string> { { "numbers", "List<int>" }, { "count", "int" }, { "item", "int" } }));
	}

	[Test]
	public void ForEachOnlyProvidesToChildren() {
		AddVariable<ForEachConstraint>(root, "item", "collection", "TestData.numbers");
		RecordingConstraint sameGameObject = AddRecording(root, "item");
		RecordingConstraint child = AddRecording(CreateChild("child", root), "item");

		Assert.That(VariableScopes.GetAvailableVariables(sameGameObject).ContainsKey("item"), Is.False);
		Assert.That(VariableScopes.GetAvailableVariables(child).ContainsKey("item"), Is.True);
	}

	[Test]
	public void UntypedCollectionsProvideObjects() {
		AddVariable<ForEachConstraint>(root, "item", "collection", "TestData.untypedNumbers");
		RecordingConstraint child = AddRecording(CreateChild("child", root), "item");
		Assert.That(VariableScopes.GetAvailableVariables(child)["item"], Is.EqualTo(typeof(object)));
	}

	[Test]
	public void ExpressionsAreParsedWithTheirScope() {
		RecordingConstraint before = AddRecording(root, "value");
		AddVariable<Variable>(root, "value", "value", "1");
		RecordingConstraint after = AddRecording(root, "value");

		Assert.That(ConstraintChecks.Check(before, true).Select(p => p.message), Is.EqualTo(new[] { "Value: Unknown identifier 'value'" }));
		Assert.That(ConstraintChecks.Check(after, true), Is.Empty);
	}

	[Test]
	public void ResultTypesAreChecked() {
		ShowConstraint show = root.AddComponent<ShowConstraint>();
		SetField(show, "condition", Input("1"));
		Assert.That(ConstraintChecks.Check(show, true).Select(p => p.message), Is.EqualTo(new[] { "Condition: The result has to be bool, but is int" }));
	}

	[TestCase("int", null)]
	[TestCase("object", null)] // a parent providing an int is fine for an expected object
	[TestCase("string", "A parent provides 'item' as int, which can never be string")]
	public void ExpectedVariablesAreCheckedAgainstTheirParents(string expectedType, string error) {
		AddVariable<ForEachConstraint>(root, "item", "collection", "TestData.numbers");
		ExpectedVariable expected = AddVariable<ExpectedVariable>(CreateChild("child", root), "item", "type", expectedType);

		List<string> problems = ConstraintChecks.Check(expected, true).Select(p => p.message).ToList();
		Assert.That(problems, error == null ? Is.Empty : Is.EqualTo(new[] { error }));
	}

	[Test]
	public void ExpectedVariablesWithoutProviderAreWarned() {
		ExpectedVariable expected = AddVariable<ExpectedVariable>(root, "item", "type", "int");
		List<ConstraintProblem> problems = ConstraintChecks.Check(expected, true);
		Assert.That(problems.Select(p => (p.severity, p.message)), Is.EqualTo(new[] { (ProblemSeverity.Warning, "No parent provides 'item' (int)") }));

		// children can use it like any other variable
		RecordingConstraint child = AddRecording(CreateChild("child", root), "item + 1");
		Assert.That(ConstraintChecks.Check(child, true), Is.Empty);
	}

	[Test]
	public void InvalidVariableNamesAreReported() {
		Variable variable = AddVariable<Variable>(root, "2fast", "value", "1");
		Assert.That(ConstraintChecks.Check(variable, true).Select(p => p.message), Is.EqualTo(new[] { "'2fast' is not a valid variable name (letters, digits and underscores, not starting with a digit)" }));
	}
}
}
