using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UIConstraints.Core.Data;
using UnityEngine;

namespace UIConstraints.Unity {
[Icon("Packages/com.lahzey.uiconstraints/Editor/Icons/render-manager.png")]
public class RenderManager : MonoBehaviour {
	private static RenderManager defaultInternal;
	public static RenderManager Default => defaultInternal ? defaultInternal : InitDefaultRenderManager(); // apparently ?? does not use the unity null override, so we have to use ? : instead

	// the default render manager if it exists, without creating it. Used while destroying, as a render manager created then would be left in the scene (e.g. when it is destroyed before the constraints while the scene is closed)
	[CanBeNull] public static RenderManager DefaultIfExists => defaultInternal ? defaultInternal : null;

	[SerializeField] private float renderInterval = 0.1f;

	public readonly List<UINode> rootUINodes = new List<UINode>();
	private readonly Dictionary<int, UINode> uiNodeMapping = new Dictionary<int, UINode>(); // maps gameObject instanceIDs to UINodes
	private float storedDeltaTime = 0f;

	private readonly DataContext rootContext = new DataContext(null); // global data comes from static members, this only holds what constraints add (e.g. ForEachConstraint variables)


	private static RenderManager InitDefaultRenderManager() {
		// create object in scene to hold the default render manager
		GameObject renderManagerObject = new GameObject("[UIConstraints] Default RenderManager");
		defaultInternal = renderManagerObject.AddComponent<RenderManager>();
		return defaultInternal;
	}

	private void Update() {
		Render();
		// storedDeltaTime += Time.unscaledDeltaTime;
		// if (storedDeltaTime >= renderInterval)
		// {
		//     storedDeltaTime = 0f;
		//     Render();
		// }
	}

	private void Render() {
		foreach (UINode uiNode in rootUINodes) uiNode.Render(rootContext);
	}

	public UINode GetUINode(int instanceID) {
		if (uiNodeMapping.ContainsKey(instanceID))
			return uiNodeMapping[instanceID];
		return null;
	}

	public UINode AddUINode(GameObject gameObject) {
		UINode uiNode = new UINode(gameObject);
		uiNodeMapping.Add(uiNode.instanceId, uiNode);

		foreach (UINode rootUINode in rootUINodes.ToList()) // use ToList() to prevent modifying the list while iterating over it, not efficient but that is not crucial here
		{
			// check if this node is a child of the root node
			if (uiNode.hierarchyPath.StartsWith(rootUINode.hierarchyPath)) {
				rootUINode.AddChild(uiNode);
				return uiNode; // return early to prevent adding this node as a root node, if this node is a child of a root node there shouldn't be any other root nodes that are a child of this node anyway
			}

			// check if the root node should be a child of this node
			if (rootUINode.hierarchyPath.StartsWith(uiNode.hierarchyPath)) {
				uiNode.AddChild(rootUINode);
				rootUINodes.Remove(rootUINode);
			}
		}

		rootUINodes.Add(uiNode);
		return uiNode;
	}

	public void RemoveUINode(UINode uiNode) {
		uiNodeMapping.Remove(uiNode.instanceId);

		// a root node, its children become root nodes (checked first, as the hierarchy path of a root node starts with itself)
		if (rootUINodes.Remove(uiNode)) {
			rootUINodes.AddRange(uiNode.Children);
			return;
		}

		foreach (UINode rootUINode in rootUINodes)
			if (uiNode.hierarchyPath.StartsWith(rootUINode.hierarchyPath)) {
				rootUINode.RemoveChild(uiNode);
				return;
			}
	}
}
}