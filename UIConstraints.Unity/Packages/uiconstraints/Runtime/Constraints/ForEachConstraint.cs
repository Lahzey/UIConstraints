using System;
using System.Collections;
using System.Collections.Generic;
using UIConstraints.Core.Data;
using UIConstraints.Core.Parsing;
using UIConstraints.Core.Parsing.Expressions;
using UnityEngine;
using UnityEngine.Serialization;

namespace UIConstraints.Unity.Constraints {
// repeats the children for each element of a collection, each copy gets its element as a variable (only the children, not the components on this game object)
[AddComponentMenu("UI Constraints/For Each Constraint")]
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/for-each.png")]
public class ForEachConstraint : UIConstraint, IVariableProvider {
	private static readonly List<object> EMPTY_LIST = new List<object>();

	[SerializeField, FormerlySerializedAs("varName")] private string variableName;
	[SerializeField, ResultType(typeof(IEnumerable)), FormerlySerializedAs("collectionConstraint")] private CodeInput collection;
	private readonly Dictionary<string, int> childIndices = new Dictionary<string, int>(); // the UINodes passed as children may not be direct children, so getting the index requires calculation which we cash here
	private readonly List<GameObject> children = new List<GameObject>();

	private GameObject[] childTemplates; // the initial children on startup, these game objects will remain disabled so we can use them as templates (a set of these for each element in the list)

	private readonly List<object> collectionContents = new List<object>();

	private Expression collectionExpression;

	public string VariableName => variableName;

	public bool ProvidesToOwnGameObject => false;

	protected override void Awake() {
		base.Awake();

		collectionExpression = ParseField(nameof(collection));

		childTemplates = new GameObject[gameObject.transform.childCount];
		for (int i = 0; i < gameObject.transform.childCount; i++) {
			GameObject child = gameObject.transform.GetChild(i).gameObject;
			child.SetActive(false);
			childTemplates[i] = child;
		}
	}

	// the element type of the collection (object for collections that are not generic)
	public IEnumerable<KeyValuePair<string, Type>> GetDeclaredVariables(IReadOnlyDictionary<string, Type> availableVariables) {
		if (!IdentifierToken.IsValidIdentifier(variableName)) yield break;

		ParseResult parseResult = CodeInputs.Parse(this, CodeInputs.GetField(GetType(), nameof(collection)), availableVariables);
		Type collectionType = parseResult.Success ? parseResult.Expression?.GetExpectedType() : null;
		if (collectionType != null) yield return new KeyValuePair<string, Type>(variableName, TypeRules.GetElementType(collectionType));
	}

	private void OnEnable() {
		node.prepareChildContext.Add(PrepareChildContext);
	}

	private void OnDisable() {
		node.prepareChildContext.Remove(PrepareChildContext);
	}

	private DataContext PrepareChildContext(UINode childNode, DataContext context) {
		if (childTemplates.Length == 0) return context;

		int childIndex = -1;
		if (childIndices.ContainsKey(childNode.hierarchyPath)) childIndex = childIndices[childNode.hierarchyPath];
		else
			for (int i = 0; i < children.Count; i++) {
				string directChildHierarchyPath = node.hierarchyPath + children[i].gameObject.GetInstanceID() + "/"; // the separator prevents id 12 from matching 123
				string distantChildHierarchyPath = childNode.hierarchyPath;
				bool startsWith = distantChildHierarchyPath.StartsWith(directChildHierarchyPath);
				if (startsWith) {
					childIndices[distantChildHierarchyPath] = i;
					childIndex = i;
					break;
				}
			}

		// the copies are created in sets of all templates per element
		int elementIndex = childIndex / childTemplates.Length;
		if (childIndex >= 0 && elementIndex < collectionContents.Count) {
			context = new DataContext(context);
			context.SetLocal(variableName, collectionContents[elementIndex]);
			return context;
		}

		// Without an element, the child can't be rendered: copies that were removed (Destroy only takes effect at the end of the frame,
		// so they are still children in the frame they are removed in) and the templates
		return null;
	}

	public override void Render(DataContext context) {
		try {
			object result = collectionExpression?.Evaluate(context) ?? EMPTY_LIST;
			if (result is not IEnumerable enumerable) {
				Debug.LogError("Collection must be of type ICollection: " + result, this);
				return;
			}

			collectionContents.Clear();
			foreach (object element in enumerable) collectionContents.Add(element);
			int countDiff = collectionContents.Count * childTemplates.Length - children.Count;
			switch (countDiff) {
				case > 0:
					for (int i = 0; i < countDiff; i++) // add new children
					{
						GameObject child = Instantiate(childTemplates[(children.Count) % childTemplates.Length], gameObject.transform);
						child.SetActive(true);
						children.Add(child);
					}

					break;
				case < 0:
					for (int i = children.Count - 1; i >= collectionContents.Count * childTemplates.Length; i--) // remove children
					{
						GameObject child = children[i];
						children.Remove(child);
						Destroy(child);
					}

					break;
			}
		}
		catch (EvaluationException e) {
			Debug.LogException(e, this);
		}
	}
}
}
