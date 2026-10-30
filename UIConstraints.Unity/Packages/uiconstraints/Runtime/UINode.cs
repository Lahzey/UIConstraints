using System;
using System.Collections.Generic;
using System.Linq;
using UIConstraints.Unity.Constraints;
using UIConstraints.Core.Data;
using UnityEngine;

namespace UIConstraints.Unity {
public class UINode {
	private readonly List<UINode> children = new List<UINode>();

	private readonly List<UIConstraint> constraints = new List<UIConstraint>(); // in component order, which is the order they are rendered in (variables are only available to the constraints after them)
	public readonly GameObject gameObject;
	public readonly string hierarchyPath;
	public readonly int instanceId;

	// prepares the context a child node is rendered with, returning null means the child is not rendered at all
	public readonly List<Func<UINode, DataContext, DataContext>> prepareChildContext = new List<Func<UINode, DataContext, DataContext>>();

	public UINode(GameObject gameObject) {
		this.gameObject = gameObject;
		hierarchyPath = GetHierarchyPath(gameObject.transform);
		instanceId = gameObject.GetInstanceID();
	}

	public void AddChild(UINode child) {
		foreach (UINode uiNode in children)
			if (child.hierarchyPath.StartsWith(uiNode.hierarchyPath)) {
				uiNode.AddChild(child);
				return;
			}

		children.Add(child);
	}

	internal IReadOnlyList<UINode> Children => children;

	// removes a node from the descendants of this one, its children take its place
	public void RemoveChild(UINode child) {
		if (children.Remove(child)) {
			foreach (UINode grandChild in child.children) AddChild(grandChild);
			return;
		}

		foreach (UINode uiNode in children)
			if (child.hierarchyPath.StartsWith(uiNode.hierarchyPath)) {
				uiNode.RemoveChild(child);
				return;
			}
	}

	public static UINode AddToNode(UIConstraint constraint) {
		RenderManager renderManager = RenderManager.Default;
		UINode node = renderManager.GetUINode(constraint.gameObject.GetInstanceID()) ?? renderManager.AddUINode(constraint.gameObject);
		node.constraints.Add(constraint);

		// Awake is not guaranteed to be called in component order
		List<UIConstraint> componentOrder = node.gameObject.GetComponents<UIConstraint>().ToList();
		node.constraints.Sort((a, b) => componentOrder.IndexOf(a).CompareTo(componentOrder.IndexOf(b)));
		return node;
	}

	public void Remove(UIConstraint constraint) {
		constraints.Remove(constraint);

		if (constraints.Count == 0) RenderManager.DefaultIfExists?.RemoveUINode(this); // without a render manager there is nothing to remove the node from
	}

	private static string GetHierarchyPath(Transform transform) {
		string path = "";
		do {
			path = transform.gameObject.GetInstanceID() + "/" + path;
		} while ((transform = transform.parent) != null);

		return path;
	}

	public void Render(DataContext context) {
		// An inactive game object is only rendered up to its show constraint, which may activate it again.
		// Without one there is nothing to do (e.g. for the disabled templates of a ForEachConstraint, which are not rendered with their variables).
		bool active = gameObject.activeInHierarchy;
		if (!active && !constraints.Any(c => c is ShowConstraint)) return;

		foreach (UIConstraint constraint in constraints)
			switch (constraint) {
				case VariableConstraint variableConstraint:
					if (!variableConstraint.TryAddVariable(ref context)) return; // logged by the constraint, everything after it may use the variable
					break;
				case ShowConstraint:
					constraint.Render(context);
					active = gameObject.activeInHierarchy;
					if (!active) return; // not rendering hidden constraints
					break;
				default:
					if (active) constraint.Render(context);
					break;
			}

		for (int i = 0; i < children.Count; i++) {
			UINode child = children[i];
			DataContext childContext = context; // each child starts from this node's context, what is prepared for one child must not leak to the next
			foreach (Func<UINode, DataContext, DataContext> prepareChildContext in prepareChildContext) {
				childContext = prepareChildContext(child, childContext);
				if (childContext == null) break; // the child must not be rendered (e.g. it is about to be destroyed)
			}

			if (childContext != null) child.Render(childContext);
		}
	}
}
}
