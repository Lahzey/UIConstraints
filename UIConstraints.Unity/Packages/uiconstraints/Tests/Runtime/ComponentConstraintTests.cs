using System.Collections;
using NUnit.Framework;
using TMPro;
using UIConstraints.Unity.Constraints;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using static UIConstraints.Unity.Tests.TestHelpers;

namespace UIConstraints.Unity.Tests {
// the properties and actions of constraints for UI components
public class ComponentConstraintTests {
	private GameObject root;

	[TearDown]
	public void TearDown() {
		Object.Destroy(root);
	}

	[UnityTest]
	public IEnumerator PropertiesAreConvertedAndSet() {
		root = new GameObject("root");
		root.SetActive(false);
		TextMeshProUGUI text = root.AddComponent<TextMeshProUGUI>();
		AddConstraint<TextConstraint>(root, "text", "1 + 2", "fontSize", "10", "color", "UnityEngine.Color.red");

		root.SetActive(true);
		yield return null;

		Assert.That(text.text, Is.EqualTo("3")); // any value, shown with ToString
		Assert.That(text.fontSize, Is.EqualTo(10f)); // the int is converted to a float
		Assert.That(text.color, Is.EqualTo(Color.red));
	}

	[UnityTest]
	public IEnumerator ActionsUseTheVariablesOfTheirRender() {
		// list: for each item in TestData.numbers, the copies have a button that stores item * 10 when clicked
		TestData.clicked = 0;
		root = new GameObject("list");
		root.SetActive(false);
		AddVariable<ForEachConstraint>(root, "item", "collection", "TestData.numbers");
		GameObject template = CreateChild("button", root);
		template.AddComponent<Image>();
		template.AddComponent<Button>();
		AddConstraint<ButtonConstraint>(template, "onClick", "TestData.clicked = item * 10", "interactable", "item != 2");

		root.SetActive(true);
		yield return null;
		yield return null; // the first render creates the copies, the second renders them

		Button second = root.transform.GetChild(2).GetComponent<Button>(); // the template is the first child
		Button third = root.transform.GetChild(3).GetComponent<Button>();
		Assert.That(second.interactable, Is.False);
		third.onClick.Invoke();
		Assert.That(TestData.clicked, Is.EqualTo(30));
	}

	[UnityTest]
	public IEnumerator InputsShowTheValueTheirActionSets() {
		TestData.volume = 0.25f;
		root = new GameObject("slider");
		root.SetActive(false);
		Slider slider = root.AddComponent<Slider>();
		AddConstraint<SliderConstraint>(root, "value", "TestData.volume", "maxValue", "2", "onValueChanged", "TestData.volume = value");

		root.SetActive(true);
		yield return null;
		Assert.That(slider.maxValue, Is.EqualTo(2f));
		Assert.That(slider.value, Is.EqualTo(0.25f));

		slider.value = 1.5f; // like dragging the slider, which notifies the listeners
		Assert.That(TestData.volume, Is.EqualTo(1.5f));

		TestData.volume = 0.5f; // changed somewhere else, which the slider follows
		yield return null;
		Assert.That(slider.value, Is.EqualTo(0.5f));
	}

	[UnityTest]
	public IEnumerator FailingActionsAreLogged() {
		root = new GameObject("button");
		root.SetActive(false);
		Button button = root.AddComponent<Button>();
		AddConstraint<ButtonConstraint>(root, "onClick", "TestData.numbers[10] = 1");

		root.SetActive(true);
		yield return null;
		LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("ArgumentOutOfRangeException"));
		button.onClick.Invoke();
	}
}
}
