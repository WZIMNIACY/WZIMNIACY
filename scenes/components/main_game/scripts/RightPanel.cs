using Godot;
using System;
using System.Runtime.CompilerServices;

public partial class RightPanel : Node
{
	[Export] public Label CurrentWordLabel;
    [Export] public VBoxContainer HistoryList;
    [Export] public ScrollContainer HistoryScroll;

	private Color _blueTeamColor = new Color("5AD2C8FF");
	private Color _redTeamColor = new Color("E65050FF");

	public void CommitToHistory(){
		if(CurrentWordLabel != null && CurrentWordLabel.Text != "" && CurrentWordLabel.Text != "-"){
            AddToHistory(CurrentWordLabel.Text, CurrentWordLabel.Modulate);
            
            CurrentWordLabel.Text = "-";
            CurrentWordLabel.Modulate = new Color(1, 1, 1, 0.5f);
        }
	}

	public void UpdateHintDisplay(string word, int count, bool isBlueTeam){
		CommitToHistory();

		if(CurrentWordLabel != null){
			CurrentWordLabel.Text = $"{word} ({count})";
            CurrentWordLabel.Modulate = isBlueTeam ? _blueTeamColor : _redTeamColor;
            CurrentWordLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		}

        CallDeferred(nameof(ScrollToBottom));
	}

	private void AddToHistory(string text, Color color)
    {
        if (HistoryList == null) return;

        Label item = new Label();
        item.Text = text;
        item.Modulate = color;
        item.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        item.SizeFlagsHorizontal = Control.SizeFlags.Expand | Control.SizeFlags.Fill;

        HistoryList.AddChild(item);
        HistoryList.AddChild(new HSeparator());

		CallDeferred(nameof(ScrollToBottom));
    }

	private void ScrollToBottom()
    {
        if (HistoryScroll != null)
        {
            var vScroll = HistoryScroll.GetVScrollBar();
            HistoryScroll.ScrollVertical = (int)vScroll.MaxValue;
        }
    }
}
