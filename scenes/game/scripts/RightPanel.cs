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



public partial class RightPanel : Node
{
	[Export] public Label currentWordLabel;
    [Export] public VBoxContainer historyList;
    [Export] public ScrollContainer historyScroll;
    [Export] public Button skipButton;
    [Export] private CardManager cardManager;

    private Color blueTeamColor = new Color("5AD2C8FF");
	private Color redTeamColor = new Color("E65050FF");

    private EOSManager eosManager;

    private MainGame mainGame;

    private Godot.Timer hintGenerationAnimationTimer;

    private CancellationTokenSource hintGeneratorCancellation;
    public sealed class HintNetworkPayload
    {
        public string Word { get; set; }
        public int Number { get; set; }
        public MainGame.Team TurnTeam { get; set; }
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

    private bool HandlePackets(P2PNetworkManager.NetMessage packet, ProductUserId fromPeer)
    {
        if (packet.type != "hint_given") return false;

        if (mainGame.isHost) return false;

        try
        {
            
            var data = packet.payload.Deserialize<HintNetworkPayload>();

            GD.Print($"[RightPanel] Received Hint: {data.Word} for {data.TurnTeam}");

            UpdateHintDisplay(data.Word, data.Number, data.TurnTeam);
            
            return true; 
        }
        catch (Exception e)
        {
            GD.PrintErr($"[RightPanel] Error parsing hint: {e.Message}");
            return true;
        }
    }

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

    public void HandleHintPacket(JsonElement payload)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = payload.Deserialize<HintNetworkPayload>(options);

            GD.Print($"[RightPanel] Packet received! Word={data.Word}, Num={data.Number}, Team={data.TurnTeam}");

            UpdateHintDisplay(data.Word, data.Number, data.TurnTeam);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[RightPanel] JSON Error: {e.Message}");
        }
    }

	public void CommitToHistory()
    {
		if(currentWordLabel != null && currentWordLabel.Text != "" && currentWordLabel.Text != "-")
        {
            AddToHistory(currentWordLabel.Text, currentWordLabel.Modulate);

            currentWordLabel.Text = "-";
            currentWordLabel.Modulate = new Color(1, 1, 1, 0.5f);
        }
	}

    public void CancelHintGeneration()
    {
        HintGenerationAnimationStop();
        hintGeneratorCancellation?.Cancel();
    }

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

        HintGenerationAnimationStop();

        string cards = string.Join(", ", hint.Cards.Select(x => x.ToString()).ToArray());
        GD.Print($"Hint Generated: {hint.Word}, {hint.NoumberOfSimilarWords}, for words: [{cards}]");
        UpdateHintDisplay(
            hint.Word,
            hint.NoumberOfSimilarWords,
            currentTurn == MainGame.Team.Blue
        );
        BroadcastHint(hint.Word, hint.NoumberOfSimilarWords, currentTurn);

        if (eosManager.currentGameMode == EOSManager.GameMode.AIvsHuman && currentTurn == MainGame.Team.Blue) // AI is assigned to Blue team
        {
            PickAiCards(hint.Word, hint.NoumberOfSimilarWords);
        }
        else
        {
            GD.Print($"[AIvsHuman] not asking AI to pick a card, gamemode={eosManager.currentGameMode == EOSManager.GameMode.AIvsHuman} turn={currentTurn == MainGame.Team.Blue}");
        }
    }

    private async void PickAiCards(string word, int numberOfCards)
    {
        int numberOfCardsLeft = numberOfCards;

        do
        {
            GD.Print($"[AIvsHuman] Asking AI to pick a card... ({numberOfCards - numberOfCardsLeft + 1}/{numberOfCards})");
            game.Card pickedCard = await mainGame.llmPlayer.PickCardFromDeck(cardManager.Deck, new Hint(word, cardManager.Deck.Cards, numberOfCardsLeft));

            GD.Print($"[AIvsHuman] AI picked card: {pickedCard.Word} {pickedCard.Team}");
            bool pickedCardIsValid = cardManager.OnCardConfirmedByAI(pickedCard);

            if (pickedCard.Team != game.Team.Blue) // break the loop if AI picked a wrong card
            {
                GD.Print("[AIvsHuman] AI picked a human's card. Ending turn.");
                return;
            }

            if (pickedCardIsValid)
            {
                GD.Print("[AIvsHuman] AI pick is valid.");
                numberOfCardsLeft--;
            }
            else
                GD.Print("[AIvsHuman] AI pick is invalid. Asking again...");
        } while (numberOfCardsLeft > 0);

        GD.Print("[AIvsHuman] AI is out of picks. Ending turn.");
        mainGame.OnSkipTurnPressed();
    }

    public void UpdateHintDisplay(string word, int number, MainGame.Team team)
    {
        bool isBlue = (team == MainGame.Team.Blue);
        
        UpdateHintDisplay(word, number, isBlue);
    }

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

	private void ScrollToBottom()
    {
        if (historyScroll != null)
        {
            var vScroll = historyScroll.GetVScrollBar();
            historyScroll.ScrollVertical = (int)vScroll.MaxValue;
        }
    }

    public void DisableSkipButton()
    {
        if (skipButton != null)
        {
            skipButton.Disabled = true;
            skipButton.MouseFilter = Control.MouseFilterEnum.Ignore;
        }
    }

    public void EnableSkipButton()
    {
        if (skipButton != null)
        {
            skipButton.Disabled = false;
            skipButton.MouseFilter = Control.MouseFilterEnum.Stop;
        }
    }

    private void HintGenerationAnimationStart()
    {
        if (hintGenerationAnimationTimer.IsStopped())
        {
            CommitToHistory();
            currentWordLabel.Text = "...";
            hintGenerationAnimationTimer.Start();
        }
    }

    private void HintGenerationAnimationStop()
    {
        if (!hintGenerationAnimationTimer.IsStopped())
        {
            hintGenerationAnimationTimer.Stop();
            currentWordLabel.Text = "";
        }
    }

    private void UpdateGenerationAnimation()
    {
        if (currentWordLabel.Text.Length >= 3)
        {
            currentWordLabel.Text = "";
            return;
        }

        currentWordLabel.Text += ".";
    }

    private async Task<Hint> GenerateHint(ILLM llm, game.Deck deck, MainGame.Team currentTurn)
    {
        return await Hint.Create(deck, llm, currentTurn.ToAiLibTeam(), oldHints[currentTurn]);
    }
}
