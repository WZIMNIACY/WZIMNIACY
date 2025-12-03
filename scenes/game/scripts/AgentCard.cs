using Godot;
using System;

public partial class AgentCard : PanelContainer
{	
	private string type;
	private bool sprawdzona = false;
	[Export]
	private CardMenager cardMenager;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		var parent = GetParent();
		cardMenager = parent.GetNode<CardMenager>("CardMenager");
		SetCardName(cardMenager.GetCardName());
		type = cardMenager.GetCardType();
		var img = GetNode<TextureRect>("AgentCard");
		SetColor(img);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
	public void SetCardName(string name){
		var label = GetNode<Label>("MarginContainer/Label");
		label.Text = name;
	}
	private void SetColor(TextureRect img){
		if(type == "blue"){
			img.Modulate = new Color("4597ffff");
		}
		else if(type == "red"){
			img.Modulate = new Color("ff627bff");
		}
		else if(type == "assassin"){
			img.Modulate = new Color("767676aa");
		}
	}
}