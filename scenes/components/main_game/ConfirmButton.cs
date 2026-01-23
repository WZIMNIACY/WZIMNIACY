using Godot;

/// <summary>
/// A custom button component that handles GUI input events to explicitly control signal emission.
/// </summary>
/// <remarks>
/// This class ensures that the <see cref="BaseButton.Pressed"/> signal is generated when clicked,
/// even in scenarios where the node's input configuration might otherwise prevent it (e.g., when set to Pass).
/// </remarks>
public partial class ConfirmButton : Button
{
    public override void _GuiInput(InputEvent @event)
    {
        base._GuiInput(@event);

        if (
            @event is InputEventMouseButton mouseButton
            && mouseButton.Pressed
            && mouseButton.ButtonIndex == MouseButton.Left
        )
        {
            // this let's the button to get the input when node input is set to PASS,
            // which let's the button generate on click signal
            AcceptEvent();
            EmitSignal(SignalName.Pressed);
        }
    }

}
