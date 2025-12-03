using Godot;
using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

public partial class CardMenager : Node
{
	private List<string> names;
	private Random rand = new Random(); 
	private int commonCards = 0;
	private int blueCards = 0;
	private int redCards = 0;
	private int assassinCards = 0;
	private string teamWithMoreCards = "blue";
	
	public void LoadNames(){
		string json = File.ReadAllText("assets/dict.json");
 		string[] name = JsonSerializer.Deserialize<string[]>(json);
		names = new List<string>(name);
	}
	[Signal]
	public delegate void UnselectCardsEventHandler();

	public void Check()
	{
		EmitSignal(SignalName.UnselectCards);
	}
	public string GetCardName(){
		if(names == null) LoadNames();
		string name = names[rand.Next(0,names.Count)];
		names.Remove(name);
		return name;
	}
	public string GetCardType(){
		//3-assassin 2-blue 1-red 0-common
		int num = rand.Next(0,4);
		if(num == 3){
			if(assassinCards == 0) {
				assassinCards += 1;
				return "assassin";
			}
			return GetCardType();
		}
		else if(num == 2){
			if(blueCards < 8 || (blueCards < 9 &&  teamWithMoreCards == "blue")){
				blueCards += 1;
				return "blue";
			}
			return GetCardType();
		}
		else if(num == 1){
			if(redCards < 8 || (redCards < 9 &&  teamWithMoreCards == "red")){
				redCards += 1;
				return "red";
			}
			return GetCardType();
		}
		else{
			if(commonCards < 7){
				commonCards += 1;
				return "common";
			}
			return GetCardType();
		}
	}
}