using game;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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

        byte id = 0;

		int currentBlueCount = 0;
        int currentRedCount = 0;
        int currentNeutralCount = 0;

		foreach (AgentCard card in GetTree().GetNodesInGroup("cards"))
		{
			card.Connect("CardSelected", new Callable(this, nameof(OnCardSelected)));
			card.Connect("CardConfirmed", new Callable(this, nameof(OnCardConfirmed)));
            card.SetId(id);
            id++;

			card.SetCard();

            switch (card.Type)
            {
                case CardType.Blue:
                    card.SetTeamIndex(currentBlueCount);
                    currentBlueCount++;
                    break;

                case CardType.Red:
                    card.SetTeamIndex(currentRedCount);
                    currentRedCount++;
                    break;

                case CardType.Common:
                    card.SetTeamIndex(currentNeutralCount);
                    currentNeutralCount++;
                    break;

                case CardType.Assassin:
                    card.SetTeamIndex(0);
                    break;
            }
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

    private void OnCardSelected(AgentCard card)
    {
        mainGame.OnCardSelected(card);
    }

    private AgentCard GetCardOfId(byte cardId)
    {
        foreach (AgentCard card in GetTree().GetNodesInGroup("cards"))
        {
            if (card.Id == cardId)
            {
                return card;
            }
        }
        GD.PrintErr($"Cant find a card with id={cardId}");
        return null;
    }

    public void ModifySelection(byte cardId, int playerIndex, bool unselect)
    {
        AgentCard card = GetCardOfId(cardId);
        if (unselect)
            card?.RemoveSelection(playerIndex);
        else
            card?.AddSelection(playerIndex);
    }

    public void ModifyAllSelections(Dictionary<byte, ushort> cardsSelections)
    {
        foreach (AgentCard card in GetTree().GetNodesInGroup("cards"))
        {
            byte cardId = card.Id!.Value;
            if (cardsSelections.ContainsKey(cardId))
            {
                card.SetSelections(cardsSelections[cardId]);
            }
            else
            {
                card.ClearSelections();
            }
        }
    }

    public Dictionary<byte, ushort> GetAllSelections()
    {
        Dictionary<byte, ushort> allSelections = new();

        foreach (AgentCard card in GetTree().GetNodesInGroup("cards"))
        {
            if (card.SelectionsCount == 0)
            {
                continue;
            }
            else
            {
                byte cardId = card.Id!.Value;
                ushort selections = card.GetSelectionsAsUshort();
                allSelections.Add(cardId, selections);
            }
        }

        return allSelections;
    }

	public void ApplyCardRevealed(AgentCard card)
	{
		if (card == null) return;

		GD.Print("Karta kliknięta: " + card.Name);
		card.SetColor();
		card.MouseFilter = MouseFilterEnum.Ignore;
		HideAllCards();

		if (Deck == null)
        {
            LoadDeck();
        }

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
	}

	public void ApplyCardConfirmedHost(AgentCard card)
	{
		ApplyCardRevealed(card);
		mainGame.CardConfirm(card);
	}

	private void OnCardConfirmed(AgentCard card)
	{
		if (card == null) return;

		// cardId = indeks slotu w GridContainer (deterministyczny na wszystkich maszynach)
		int cardId = card.GetIndex();

		if (!mainGame.isHost)
		{
			// Klient nie wykonuje logiki i nie zmienia lokalnie decka/UI.
			// Klient tylko prosi hosta (jak skip turn).
			mainGame.OnCardConfirmPressedClient(cardId);
			return;
		}

		// Host też przechodzi przez wspólną ścieżkę (broadcast do klientów + logika lokalna)
		mainGame.HostConfirmCardAndBroadcast(cardId, eosManager.localProductUserIdString);
	}

    public CardType? OnCardConfirmedByAI(game.Card confirmedCard)
    {
        foreach (AgentCard card in GetTree().GetNodesInGroup("cards"))
        {
            if (card.cardInfo.Word == confirmedCard.Word) // TODO: check if the card is already selected
            {
                OnCardConfirmed(card);
                return card.Type; // zwracam typ bo libka AI zwraca zle typy kart
            }
        }

        GD.PrintErr("AI confirmed card not found among AgentCards");
        return null;
    }

    private void HideAllCards()
	{
		foreach (AgentCard card in GetTree().GetNodesInGroup("cards"))
		{
			card.ClearSelections();
		}
	}
}
