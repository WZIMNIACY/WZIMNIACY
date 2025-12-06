using Godot;

public partial class RightPanel : Node
{
	[Export] public Label currentWordLabel;
    [Export] public VBoxContainer historyList;
    [Export] public ScrollContainer historyScroll;

	private Color blueTeamColor = new Color("5AD2C8FF");
	private Color redTeamColor = new Color("E65050FF");

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
}
