#if TOOLS
using Godot;

namespace UIConstraints.Godot {
// Entry point of the editor plugin. Nothing is registered yet, the Godot integration (constraint nodes, inspector with code completion) is still to be written.
[Tool]
public partial class UIConstraintsPlugin : EditorPlugin {
	public override void _EnterTree() { }

	public override void _ExitTree() { }
}
}
#endif
