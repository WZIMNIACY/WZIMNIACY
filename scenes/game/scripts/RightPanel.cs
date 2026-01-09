using Godot;
using Epic.OnlineServices;

public partial class RightPanel : Node
{
	[Export] public Label currentWordLabel;
    [Export] public VBoxContainer historyList;
    [Export] public ScrollContainer historyScroll;
    [Export] public Button skipButton;

	private Color blueTeamColor = new Color("5AD2C8FF");
	private Color redTeamColor = new Color("E65050FF");

    private EOSManager eosManager;
    private P2PNetworkManager p2pNet;
    private MainGame mainGame;

    public override void _Ready()
    {
        eosManager = GetNodeOrNull<EOSManager>("/root/EOSManager");
        p2pNet = GetNodeOrNull<P2PNetworkManager>("/root/P2PNetworkManager");
        if (p2pNet == null) p2pNet = GetNodeOrNull<P2PNetworkManager>("../P2PNetworkManager");

        mainGame = GetNodeOrNull<MainGame>("/root/MainGame") ?? GetParent<MainGame>(); 
    }

    public void BroadcastHint(string word, int number, MainGame.Team team)
    {
        if (mainGame != null && mainGame.isHost && p2pNet != null)
        {
            var payload = new MainGame.HintNetworkPayload
            {
                Word = word,
                Number = number,
                TeamId = team
            };

            var members = eosManager.GetCurrentLobbyMembers();
            foreach (var member in members)
            {
                string puid = member["userId"].ToString();
                if (puid != eosManager.localProductUserIdString)
                {
                    var targetPeer = ProductUserId.FromString(puid);
                    p2pNet.SendRpcToPeer(targetPeer, "hint_given", payload);
                }
            }
            GD.Print($"[RightPanel] Broadcasted hint: {word} ({team})");
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

	public void UpdateHintDisplay(string word, int count, bool isBlueTeam)
    {
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
}
