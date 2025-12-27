using Godot;
using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

public partial class CardManager : GridContainer
{
	[Signal] public delegate void CardManagerReadyEventHandler();

	[Export] private MainGame mainGame;

	private List<string> names;
	private Random rand = new Random(); 
	private int commonCards = 0;
	private int blueCards = 0;
	private int redCards = 0;
	private int assassinCards = 0;
	
	public MainGame.Team teamWithMoreCards = MainGame.Team.None;
	
	public enum CardType
	{	
		Red,
		Blue,
		Assassin,
		Common
	}

	public override void _Ready()
	{
		base._Ready();
		mainGame.GameReady += OnGameReady;
		foreach (var card in GetTree().GetNodesInGroup("cards"))
		{
			card.Connect("CardConfirmed", new Callable(this, nameof(OnCardConfirmed)));
        }
	}

	private void OnGameReady()
	{
		EmitSignal(SignalName.CardManagerReady);
	}

	public void LoadNames(){
		string json = File.ReadAllText("assets/dict.json");
 		string[] name = JsonSerializer.Deserialize<string[]>(json);
		names = new List<string>(name);
	}

	public string GetCardName(){
		if(names == null) LoadNames();
		string name = names[rand.Next(0,names.Count)];
		names.Remove(name);
		return name;
	}
	
	public CardType GetCardType(){
		//3-assassin 2-blue 1-red 0-common
		if(teamWithMoreCards == MainGame.Team.None)
		{
			teamWithMoreCards = mainGame.StartingTeam;
		}
		int num = rand.Next(0,4);
		if(num == 3){
			if(assassinCards == 0) {
				assassinCards += 1;
				return CardType.Assassin;
			}
			return GetCardType();
		}
		else if(num == 2){
			if(blueCards < 8 || (blueCards < 9 &&  teamWithMoreCards == MainGame.Team.Blue)){
				blueCards += 1;
				return CardType.Blue;
			}
			return GetCardType();
		}
		else if(num == 1){
			if(redCards < 8 || (redCards < 9 &&  teamWithMoreCards == MainGame.Team.Red)){
				redCards += 1;
				return CardType.Red;
			}
			return GetCardType();
		}
		else{
			if(commonCards < 7){
				commonCards += 1;
				return CardType.Common;
			}
			return GetCardType();
		}
	}
	
	private void OnCardConfirmed(AgentCard card)
	{
		GD.Print("Karta kliknięta: " + card.Name);
		HideAllCards();
        mainGame.CardConfirm(card);
    }

	private void HideAllCards()
	{
		foreach (AgentCard card in GetTree().GetNodesInGroup("cards"))
		{
			card.Unselect();
		}
	}
}
