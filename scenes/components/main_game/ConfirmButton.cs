using Godot;
using System;

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
