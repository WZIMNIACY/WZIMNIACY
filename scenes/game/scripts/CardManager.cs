using Godot;
using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

public partial class CardManager : GridContainer
{
    public const int DECK_CARD_COUNT = 25;

	[Signal] public delegate void CardManagerReadyEventHandler();

	[Export] private MainGame mainGame;

	private EOSManager eosManager;
	private Random rand;

    public game.Deck Deck { get; private set; }
    private Dictionary<string, List<double>> namesVectorDb;
    private int takenCards = 0;
	private int commonCards = 0;
	private int blueCards = 0;
	private int redCards = 0;
	private int assassinCards = 0;

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

		eosManager = GetNode<EOSManager>("/root/EOSManager");
		rand = new Random((int)eosManager.CurrentGameSession.Seed);

        LoadDeck();

		foreach (var card in GetTree().GetNodesInGroup("cards"))
		{
			card.Connect("CardConfirmed", new Callable(this, nameof(OnCardConfirmed)));
		}
	}

	private void OnGameReady()
	{
		EmitSignal(SignalName.CardManagerReady);
	}

	public void LoadDeck(){
        string json = File.ReadAllText("assets/WordVectorBase.json");
        namesVectorDb = JsonSerializer.Deserialize<Dictionary<string, List<double>>>(json);
        game.Team cardTeam = mainGame.StartingTeam == MainGame.Team.Red
            ? game.Team.Red
            : game.Team.Blue;

        Deck = game.Deck.CreateFromDictionary(namesVectorDb, cardTeam, rand);
	}

    public game.Card TakeCard()
    {
        if (Deck == null)
        {
            LoadDeck();
        }

        if (takenCards > DECK_CARD_COUNT)
        {
            throw new Exception("Out of cards to take - taken too many");
        }

        game.Card card = Deck.Cards[takenCards];
        takenCards += 1;
        return card;
    }

	private void OnCardConfirmed(AgentCard card)
	{
		GD.Print("Karta kliknięta: " + card.Name);
		card.SetColor();
		card.MouseFilter = MouseFilterEnum.Ignore;
		HideAllCards();

        string cardName = card.cardInfo.Word;
        GD.Print($"About to delete {cardName} card from deck");
        for (var i = 0; i < Deck.Cards.Count; i++)
        {
            if (Deck.Cards[i].Word == cardName)
            {
                GD.Print($"Deleting card {cardName} from deck");
                Deck.Cards.SwapRemove(i);
                break;
            }
        }

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
