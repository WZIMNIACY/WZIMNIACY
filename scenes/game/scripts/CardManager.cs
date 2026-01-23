using game;
using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Manages the card grid, deck operations, and card interactions in the game.
/// Handles card selection, confirmation, and synchronization between host and clients.
/// </summary>
public partial class CardManager : GridContainer
{
	/// <summary>
	/// The total number of cards in the deck.
	/// </summary>
	public const int DECK_CARD_COUNT = 25;

	/// <summary>
	/// Emitted when the CardManager is ready and the game has started.
	/// </summary>
	[Signal] public delegate void CardManagerReadyEventHandler();

	/// <summary>
	/// The main game controller.
	/// </summary>
	[Export] private MainGame mainGame;

	/// <summary>
	/// Manager for Epic Online Services integration.
	/// </summary>
	private EOSManager eosManager;
	
	/// <summary>
	/// Random number generator for deck shuffling.
	/// </summary>
	private Random rand;

	/// <summary>
	/// The current deck of cards in play.
	/// </summary>
	public game.Deck Deck { get; private set; }
	
	/// <summary>
	/// Database of words and their vector representations used for generating the deck.
	/// </summary>
	private Dictionary<string, List<double>> namesVectorDb;
	
	/// <summary>
	/// Counter for the number of cards taken from the deck.
	/// </summary>
	private int takenCards = 0;

	/// <summary>Count of common (neutral) cards assigned.</summary>
	private int commonCards = 0;
	/// <summary>Count of blue team cards assigned.</summary>
	private int blueCards = 0;
	/// <summary>Count of red team cards assigned.</summary>
	private int redCards = 0;
	/// <summary>Count of assassin cards assigned.</summary>
	private int assassinCards = 0;

	/// <summary>
	/// Representative types of cards in the game.
	/// </summary>
	public enum CardType
	{
		/// <summary>Red team card.</summary>
		Red,
		/// <summary>Blue team card.</summary>
		Blue,
		/// <summary>Assassin card (instant loss if revealed).</summary>
		Assassin,
		/// <summary>Common (neutral) card.</summary>
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

	/// <summary>
	/// Called when the main game is ready.
	/// Emits the <see cref="CardManagerReady"/> signal.
	/// </summary>
	private void OnGameReady()
	{
		EmitSignal(SignalName.CardManagerReady);
	}

	/// <summary>
	/// Loads the word vector database and initializes the deck based on the starting team and seed.
	/// </summary>
	public void LoadDeck(){
		string json = FileAccess.GetFileAsString("res://assets/WordVectorBase.json");
		namesVectorDb = JsonSerializer.Deserialize<Dictionary<string, List<double>>>(json);
		game.Team cardTeam = mainGame.StartingTeam == MainGame.Team.Red
			? game.Team.Red
			: game.Team.Blue;

		Deck = game.Deck.CreateFromDictionary(namesVectorDb, cardTeam, rand);
	}

    /// <summary>
    /// Retrieves the next available card from the deck.
    /// </summary>
    /// <returns>The next <see cref="game.Card"/> from the deck.</returns>
    /// <exception cref="Exception">Thrown if requested more cards than <see cref="DECK_CARD_COUNT"/>.</exception>
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

    /// <summary>
    /// Handles the selection of a card by a user.
    /// Relays the event to the MainGame controller.
    /// </summary>
    /// <param name="card">The card that was selected.</param>
    private void OnCardSelected(AgentCard card)
    {
        mainGame.OnCardSelected(card);
    }

    /// <summary>
    /// Finds an <see cref="AgentCard"/> node by its unique ID.
    /// </summary>
    /// <param name="cardId">The ID of the card to find.</param>
    /// <returns>The <see cref="AgentCard"/> if found, otherwise null.</returns>
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

    /// <summary>
    /// Modifies the selection state of a specific card for a player.
    /// </summary>
    /// <param name="cardId">The ID of the card to modify.</param>
    /// <param name="playerIndex">The index of the player changing selection.</param>
    /// <param name="unselect">True to remove selection, false to add it.</param>
    public void ModifySelection(byte cardId, int playerIndex, bool unselect)
    {
        AgentCard card = GetCardOfId(cardId);
        if (unselect)
            card?.RemoveSelection(playerIndex);
        else
            card?.AddSelection(playerIndex);
    }

    /// <summary>
    /// Updates the selection state of all cards based on a provided dictionary.
    /// Used for synchronizing selections from the host.
    /// </summary>
    /// <param name="cardsSelections">A dictionary mapping card IDs to their selection bitmasks.</param>
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

    /// <summary>
    /// Retrieves the current selection states of all cards.
    /// </summary>
    /// <returns>A dictionary mapping card IDs to their selection bitmasks, excluding cards with no selections.</returns>
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

	/// <summary>
	/// Applies visual and logical changes when a card is revealed/confirmed.
	/// </summary>
	/// <param name="card">The card that was revealed.</param>
	public void ApplyCardRevealed(AgentCard card)
	{
		if (card == null) return;

		GD.Print("Karta kliknięta: " + card.Name);
		card.SetColor();
		card.MouseFilter = MouseFilterEnum.Ignore;
        card.ClearSelections();

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

	/// <summary>
	/// Handles card confirmation logic on the host side.
	/// Applies the reveal locally and notifies the MainGame controller.
	/// </summary>
	/// <param name="card">The card being confirmed.</param>
	public void ApplyCardConfirmedHost(AgentCard card)
	{
		ApplyCardRevealed(card);
		mainGame.CardConfirm(card);
	}

	/// <summary>
	/// Handles the event when a card is confirmed (clicked for reveal).
	/// </summary>
	/// <param name="card">The card that was clicked.</param>
	private void OnCardConfirmed(AgentCard card)
	{
		GD.Print($"[CardManager] OnCardConfirmed fired for cardIndex={card?.GetIndex()} name={card?.Name} isHost={mainGame?.isHost}");

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

    /// <summary>
    /// Handles card confirmation triggered by an AI agent.
    /// </summary>
    /// <param name="confirmedCard">The card data provided by the AI.</param>
    /// <returns>The <see cref="CardType"/> of the confirmed card, or null if not found.</returns>
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

	/// <summary>
	/// Clears the selection state of all cards in the grid.
	/// </summary>
	public void ClearAllSelections()
	{
		foreach (AgentCard card in GetTree().GetNodesInGroup("cards"))
		{
			card.ClearSelections();
		}
	}
}
