using Godot;
using System;

public partial class CardMenager : Node
{
	[Signal]
	public delegate void UnselectCardsEventHandler();

	public void Check()
	{
		EmitSignal(SignalName.UnselectCards);
	}
}
