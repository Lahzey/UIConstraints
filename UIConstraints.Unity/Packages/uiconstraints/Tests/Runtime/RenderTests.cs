using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UIConstraints.Unity.Constraints;
using UnityEngine;
using UnityEngine.TestTools;
using static UIConstraints.Unity.Tests.TestHelpers;

namespace UIConstraints.Unity.Tests {
public class RenderTests {
	private GameObject root;

	[TearDown]
	public void TearDown() {
		if (root != null) Object.Destroy(root);
	}

	[UnityTest]
	public IEnumerator DestroyingConstraintsDoesNotCreateARenderManager() {
		// when a scene is closed, the render manager may be destroyed before the constraints, which must not create a new one (it would be left in the scene)
		root = new GameObject("root");
		AddRecording(root, "1");
		yield return null;

		Object.DestroyImmediate(RenderManager.Default.gameObject);
		Object.DestroyImmediate(root);
		Assert.That(RenderManager.DefaultIfExists, Is.Null);
	}

	[UnityTest]
	public IEnumerator VariablesFlowIntoForEachChildren() {
		// root: numbers = TestData.numbers, then a recording using it (after the variable on the same game object)
		//   list: for each item in numbers
		//     template: doubled = item * 2, then a recording of doubled + numbers.Count
		root = new GameObject("root");
		root.SetActive(false); // so Awake is only called once everything is set up
		AddVariable<Variable>(root, "numbers", "value", "TestData.numbers");
		RecordingConstraint rootRecording = AddRecording(root, "numbers.Count");

		GameObject list = CreateChild("list", root);
		AddVariable<ForEachConstraint>(list, "item", "collection", "numbers");

		GameObject template = CreateChild("template", list);
		AddVariable<Variable>(template, "doubled", "value", "item * 2");
		AddRecording(template, "doubled + numbers.Count");

		root.SetActive(true);
		yield return null;
		yield return null; // the first render creates the copies, the second renders them

		Assert.That(rootRecording.LastValue, Is.EqualTo(3));
		List<RecordingConstraint> copies = Enumerable.Range(0, list.transform.childCount).Select(i => list.transform.GetChild(i).gameObject).Where(g => g.activeSelf).Select(g => g.GetComponent<RecordingConstraint>()).ToList();
		Assert.That(copies.Select(c => c.LastValue), Is.EqualTo(new object[] { 5, 7, 9 }));
		Assert.That(template.GetComponent<RecordingConstraint>().RenderCount, Is.Zero); // the disabled template is not rendered
	}

	[UnityTest]
	public IEnumerator RemovingElementsDoesNotRenderRemovedCopies() {
		TestData.changingNumbers = new List<int> { 1, 2, 3 };
		root = new GameObject("root");
		root.SetActive(false);
		AddVariable<ForEachConstraint>(root, "item", "collection", "TestData.changingNumbers");
		GameObject template = CreateChild("template", root);
		AddRecording(template, "item * 10");

		root.SetActive(true);
		yield return null;
		yield return null;

		// the removed copy still exists until the end of the frame it is removed in, rendering it without its item would log an exception (failing this test)
		TestData.changingNumbers.RemoveAt(1);
		yield return null;
		yield return null;

		List<object> values = Enumerable.Range(0, root.transform.childCount).Select(i => root.transform.GetChild(i).gameObject).Where(g => g.activeSelf).Select(g => g.GetComponent<RecordingConstraint>().LastValue).ToList();
		Assert.That(values, Is.EqualTo(new object[] { 10, 30 }));
	}

	[UnityTest]
	public IEnumerator FailingExpectedVariableStopsRendering() {
		root = new GameObject("root");
		root.SetActive(false);
		ExpectedVariable expected = root.AddComponent<ExpectedVariable>();
		SetField(expected, "variableName", "missing");
		SetField(expected, "type", Input("int"));
		RecordingConstraint recording = AddRecording(root, "missing + 1");

		LogAssert.Expect(LogType.Exception, new Regex("No parent provides the variable 'missing'"));
		root.SetActive(true);
		yield return null;
		yield return null; // logged only once

		Assert.That(recording.RenderCount, Is.Zero);
	}
}
}
