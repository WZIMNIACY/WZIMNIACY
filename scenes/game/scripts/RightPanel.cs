using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Godot;
using hints;
using AI;
using System.Text.Json;
using Epic.OnlineServices;
using System;


/// <summary>
/// Manages the right-side game panel, responsible for displaying game history,
/// handling hint generation and broadcasting, and managing the AI's turn actions.
/// </summary>
public partial class RightPanel : Node
{
	/// <summary>
	/// Label displaying the currently active word or hint being processed.
	/// </summary>
	[Export] public Label currentWordLabel;
    
    /// <summary>
    /// Container for the list of past game events and hints.
    /// </summary>
    [Export] public VBoxContainer historyList;

    /// <summary>
    /// Scroll container for the history list.
    /// </summary>
    [Export] public ScrollContainer historyScroll;

    /// <summary>
    /// Button to skip the current turn.
    /// </summary>
    [Export] public Button skipButton;

    /// <summary>
    /// Reference to the CardManager for interacting with game cards.
    /// </summary>
    [Export] private CardManager cardManager;

    private Color blueTeamColor = new Color("5AD2C8FF");
	private Color redTeamColor = new Color("E65050FF");

    private EOSManager eosManager;

    private MainGame mainGame;

    private Hint lastGeneratedHint;

    /// <summary>
    /// Gets the most recently generated AI hint.
    /// </summary>
    public Hint LastGeneratedHint => lastGeneratedHint;

    /// <summary>
    /// Manually sets the last generated hint. 
    /// Useful for reconstructing state when receiving a hint over the network without AI generation.
    /// </summary>
    /// <param name="word">The hint word.</param>
    /// <param name="number">The number of associated cards.</param>
    public void SetLastGeneratedHint(string word, int number)
    {
        // Minimalny hint do reakcji: Word + Number wystarczy do budowy tekstu.
        // Cards dajemy pustą listę, żeby nie ryzykować nulli w Reaction.create().
        lastGeneratedHint = new hints.Hint(
            word,
            new List<game.Card>(),
            number
        );
    }

    private Godot.Timer hintGenerationAnimationTimer;

    private CancellationTokenSource hintGeneratorCancellation;

    /// <summary>
    /// Payload structure for broadcasting hints over the network.
    /// </summary>
    public sealed class HintNetworkPayload
    {
        public string Word { get; set; }
        public int Number { get; set; }
        public MainGame.Team TurnTeam { get; set; }
    }

    /// <summary>
    /// Represents the internal state of the hint generation animation.
    /// </summary>
    public sealed class AnimationState
    {
        public bool IsAnimating { get; set; }
    }
    private Dictionary<MainGame.Team, List<string>> oldHints;

    public override void _Ready()
    {
        oldHints = new Dictionary<MainGame.Team, List<string>>();
        oldHints.Add(MainGame.Team.Red, new List<string>());
        oldHints.Add(MainGame.Team.Blue, new List<string>());
        hintGenerationAnimationTimer = new Godot.Timer();
        hintGenerationAnimationTimer.WaitTime = 0.5f;
        hintGenerationAnimationTimer.OneShot = false;
        hintGenerationAnimationTimer.Autostart = false;
        hintGenerationAnimationTimer.Timeout += UpdateGenerationAnimation;
        AddChild(hintGenerationAnimationTimer);

        eosManager = GetNodeOrNull<EOSManager>("/root/EOSManager");

        mainGame = GetTree().CurrentScene as MainGame;

        CallDeferred(nameof(SubscribeToNetwork));

    }

    public override void _ExitTree()
    {
        CancelHintGeneration();
        base._ExitTree();
    }

    /// <summary>
    /// Subscribes to network packet events from the P2P manager.
    /// </summary>
    private void SubscribeToNetwork()
    {
        if (mainGame != null && mainGame.P2PNet != null)
        {
            mainGame.P2PNet.PacketHandlers += HandlePackets;
            GD.Print("[RightPanel] Successfully subscribed to P2P packets.");
        }
        else
        {
            GD.PrintErr("[RightPanel] CRITICAL: Could not subscribe to network. MainGame or P2PNet is null.");
        }
    }

    /// <summary>
    /// Handles incoming network packets related to hints.
    /// </summary>
    /// <param name="packet">The received network packet.</param>
    /// <param name="fromPeer">The ID of the peer who sent the packet.</param>
    /// <returns>True if the packet was handled, false otherwise.</returns>
    private bool HandlePackets(P2PNetworkManager.NetMessage packet, ProductUserId fromPeer)
    {
        if (packet.type != "hint_given") return false;

        if (mainGame.isHost) return false;

        try
        {
            var data = packet.payload.Deserialize<HintNetworkPayload>();

            GD.Print($"[RightPanel] Received Hint: {data.Word} for {data.TurnTeam}");
            HintGenerationAnimationStop();
            UpdateHintDisplay(data.Word, data.Number, data.TurnTeam);

            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[RightPanel] Error parsing hint: {e.Message}");
            return true;
        }
    }

    /// <summary>
    /// Broadcasts a generated hint to all connected clients.
    /// Only the host can perform this action.
    /// </summary>
    /// <param name="word">The hint word to broadcast.</param>
    /// <param name="number">The number associated with the hint.</param>
    /// <param name="team">The team for which the hint is intended.</param>
    public void BroadcastHint(string word, int number, MainGame.Team team)
    {
        if (!mainGame.isHost)
        {
            return;
        }

        var net = mainGame.P2PNet;

        if (net != null)
        {
            var payload = new HintNetworkPayload
            {
                Word = word,
                Number = number,
                TurnTeam = team
            };

            net.SendRpcToAllClients("hint_given", payload);
            GD.Print($"[RightPanel] Broadcasted hint: {word} (Team: {team})");
        }
    }

    /// <summary>
    /// Moves the current displayed word to the history list and resets the display.
    /// </summary>
	public void CommitToHistory()
    {
		if(currentWordLabel != null && currentWordLabel.Text != "" && !IsLabelDirty())
        {
            AddToHistory(currentWordLabel.Text, currentWordLabel.Modulate);

            currentWordLabel.Text = "-";
            currentWordLabel.Modulate = new Color(1, 1, 1, 0.5f);
        }
	}

    /// <summary>
    /// Cancels any ongoing hint generation process.
    /// </summary>
    public void CancelHintGeneration()
    {
        HintGenerationAnimationStop();
        hintGeneratorCancellation?.Cancel();
    }

    /// <summary>
    /// Generates a hint using the LLM and updates the UI.
    /// Also handles AI card picking if it's an AI vs Human game.
    /// </summary>
    /// <param name="llm">The LLM interface to use for generation.</param>
    /// <param name="deck">The current state of the card deck.</param>
    /// <param name="currentTurn">The team whose turn it is.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task GenerateAndUpdateHint(ILLM llm, game.Deck deck, MainGame.Team currentTurn)
    {
        CancelHintGeneration();
        hintGeneratorCancellation?.Dispose();
        hintGeneratorCancellation = new CancellationTokenSource();
        CancellationToken ct = hintGeneratorCancellation.Token;
        HintGenerationAnimationStart();
        Hint hint = await GenerateHint(llm, deck, currentTurn);

        // It's ok that i only check here because after GenerateHint there are no await,
        // so execution will not be taken away.
        // After this if, do not call await,
        // because after we are given back the execution we might have been cancelled
        if (ct.IsCancellationRequested)
        {
            return;
        }

        //zapisujemy ostatnio wygenerowaną podpowiedź
        lastGeneratedHint = hint;
        HintGenerationAnimationStop();

        string cards = string.Join(", ", hint.Cards.Select(x => x.ToString()).ToArray());
        GD.Print($"Hint Generated: {hint.Word}, {hint.NoumberOfSimilarWords}, for words: [{cards}]");
        UpdateHintDisplay(
            hint.Word,
            hint.NoumberOfSimilarWords,
            currentTurn == MainGame.Team.Blue
        );
        BroadcastHint(hint.Word, hint.NoumberOfSimilarWords, currentTurn);

        if (eosManager.currentGameMode == EOSManager.GameMode.AIvsHuman && currentTurn == MainGame.Team.Red) // AI is assigned to Red team
        {
            PickAiCards(hint.Word, hint.NoumberOfSimilarWords);
        }
        else
        {
            GD.Print($"[AIvsHuman] not asking AI to pick a card, gamemode={eosManager.currentGameMode == EOSManager.GameMode.AIvsHuman} turn={currentTurn == MainGame.Team.Blue}");
        }
    }

    /// <summary>
    /// Logic for the AI to pick cards based on the generated hint.
    /// </summary>
    /// <param name="word">The hint word.</param>
    /// <param name="numberOfCards">The number of cards to pick.</param>
    private async void PickAiCards(string word, int numberOfCards)
    {
        int numberOfCardsLeft = numberOfCards;

        do
        {
            GD.Print($"[AIvsHuman] Asking AI to pick a card... ({numberOfCards - numberOfCardsLeft + 1}/{numberOfCards})");
            game.Card pickedCard = await mainGame.llmPlayer.PickCardFromDeck(cardManager.Deck, new Hint(word, null, numberOfCardsLeft));

            GD.Print($"[AIvsHuman] AI picked card: {pickedCard.Word} {pickedCard.Team}");
            CardManager.CardType? pickedCardType = cardManager.OnCardConfirmedByAI(pickedCard);

            if (pickedCardType != CardManager.CardType.Red) // break the loop if AI picked a wrong card
            {
                GD.Print("[AIvsHuman] AI picked a wrong card. Ending turn.");
                return;
            }

            if (pickedCardType is not null)
            {
                GD.Print("[AIvsHuman] AI pick is valid.");
                numberOfCardsLeft--;
            }
            else
            {
                GD.Print("[AIvsHuman] AI pick is invalid. Asking again...");
            }

        } while (numberOfCardsLeft > 0 && !mainGame.isGameFinished);

        GD.Print("[AIvsHuman] AI is out of picks. Ending turn.");
        mainGame.OnSkipTurnPressed();
    }

    /// <summary>
    /// Updates the display with a new hint.
    /// </summary>
    /// <param name="word">The hint word.</param>
    /// <param name="number">The number of cards.</param>
    /// <param name="team">The team to display the hint for.</param>
    public void UpdateHintDisplay(string word, int number, MainGame.Team team)
    {
        bool isBlue = (team == MainGame.Team.Blue);

        UpdateHintDisplay(word, number, isBlue);
    }

    /// <summary>
    /// Updates the display with a new hint.
    /// </summary>
    /// <param name="word">The hint word.</param>
    /// <param name="count">The number of cards.</param>
    /// <param name="isBlueTeam">True if it is the Blue team's hint, false for Red team.</param>
	public void UpdateHintDisplay(string word, int count, bool isBlueTeam)
    {
        MainGame.Team team = isBlueTeam ? MainGame.Team.Blue : MainGame.Team.Red;
        oldHints[team].Add(word);

        CommitToHistory();

		if(currentWordLabel != null)
        {
			currentWordLabel.Text = $"{word} ({count})";
            currentWordLabel.Modulate = isBlueTeam ? blueTeamColor : redTeamColor;
            currentWordLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		}

        CallDeferred(nameof(ScrollToBottom));
	}

    /// <summary>
    /// Adds an entry to the visual history list.
    /// </summary>
    /// <param name="text">The text to add.</param>
    /// <param name="color">The color of the text.</param>
	private void AddToHistory(string text, Color color)
    {
        if (historyList == null) return;

        Label item = new Label();
        item.Text = text;
        item.Modulate = color;
        item.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        item.SizeFlagsHorizontal = Control.SizeFlags.Expand | Control.SizeFlags.Fill;

        historyList.AddChild(item);
        historyList.AddChild(new HSeparator());

		CallDeferred(nameof(ScrollToBottom));
    }

    /// <summary>
    /// Scrolls the history container to the bottom.
    /// </summary>
	private void ScrollToBottom()
    {
        if (historyScroll != null)
        {
            var vScroll = historyScroll.GetVScrollBar();
            historyScroll.ScrollVertical = (int)vScroll.MaxValue;
        }
    }

    /// <summary>
    /// Disables the "Skip Turn" button.
    /// </summary>
    public void DisableSkipButton()
    {
        if (skipButton != null)
        {
            skipButton.Disabled = true;
            skipButton.MouseFilter = Control.MouseFilterEnum.Ignore;
        }
    }

    /// <summary>
    /// Enables the "Skip Turn" button.
    /// </summary>
    public void EnableSkipButton()
    {
        if (skipButton != null)
        {
            skipButton.Disabled = false;
            skipButton.MouseFilter = Control.MouseFilterEnum.Stop;
        }
    }

    /// <summary>
    /// Starts the hint generation loading animation.
    /// </summary>
    public void HintGenerationAnimationStart()
    {
        if (hintGenerationAnimationTimer.IsStopped())
        {
            CommitToHistory();
            currentWordLabel.Text = "...";
            hintGenerationAnimationTimer.Start();
        }
    }

    /// <summary>
    /// Stops the hint generation loading animation.
    /// </summary>
    private void HintGenerationAnimationStop()
    {
        hintGenerationAnimationTimer.Stop();

        if (IsLabelDirty())
        {
            currentWordLabel.Text = "";
        }
    }

    /// <summary>
    /// Checks if the label contains temporary animation text.
    /// </summary>
    /// <returns>True if the label text is temporary ("." or "-").</returns>
    private bool IsLabelDirty()
    {
        return currentWordLabel.Text.StartsWith(".") || currentWordLabel.Text == "-";
    }

    /// <summary>
    /// Updates the loading dots animation.
    /// </summary>
    private void UpdateGenerationAnimation()
    {
        if (currentWordLabel.Text.Length >= 3)
        {
            currentWordLabel.Text = "";
            return;
        }

        currentWordLabel.Text += ".";
    }

    /// <summary>
    /// Internal helper to generate a hint (with retries if needed).
    /// </summary>
    /// <param name="llm">The LLM interface.</param>
    /// <param name="deck">The current deck.</param>
    /// <param name="currentTurn">The current team.</param>
    /// <returns>A generated Hint object.</returns>
    private async Task<Hint> GenerateHint(ILLM llm, game.Deck deck, MainGame.Team currentTurn)
    {
        Hint hint = null;
        const uint GENERATE_HINT_MAX_TRIES = 3;
        for (int i = 0; i < GENERATE_HINT_MAX_TRIES; i++)
        {
            hint = await Hint.Create(deck, llm, currentTurn.ToAiLibTeam(), oldHints[currentTurn]);
            if (!oldHints[currentTurn].Contains(hint.Word))
            {
                break;
            }
            GD.Print($"Generated already shown hint. Try {i + 1} out of {GENERATE_HINT_MAX_TRIES}");
        }
        return hint;
    }
}
