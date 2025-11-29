using Godot;
using System;

public partial class Mediator : Node
{
	[Signal]
	public delegate void UnselectCardsEventHandler();

	public void Check()
	{
		EmitSignal(SignalName.UnselectCards);
	}
}
