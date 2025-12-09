using Godot;

public partial class CardMenager : GridContainer
{
	public override void _Ready()
	{
		base._Ready();
		foreach (var card in GetTree().GetNodesInGroup("cards"))
		{
			card.Connect("CardConfirmed", new Callable(this, nameof(OnCardConfirmed)));
		}
	}

	private void OnCardConfirmed(AgentCardAnimation card)
	{
		GD.Print("Karta kliknięta: " + card.Name);
		HideAllCards();
	}

	private void HideAllCards()
	{
		foreach (AgentCardAnimation card in GetTree().GetNodesInGroup("cards"))
		{
			card.Unselect();
		}
	}
}
