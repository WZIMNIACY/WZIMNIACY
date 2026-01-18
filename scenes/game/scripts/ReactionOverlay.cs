using Godot;

public partial class ReactionOverlay : CanvasLayer
{
	private Control reactionBubble;
	private Label reactionLabel;

	private SceneTreeTimer hideTimer;

	public override void _Ready()
	{
		reactionBubble = GetNodeOrNull<Control>("ReactionBubble");
		reactionLabel = GetNodeOrNull<Label>("ReactionBubble/MarginContainer/ReactionLabel");

		if (reactionBubble == null || reactionLabel == null)
		{
			GD.PrintErr("[ReactionOverlay] CRITICAL: Missing nodes. Expected 'ReactionBubble' and 'ReactionBubble/ReactionLabel'. Overlay disabled.");
			return;
		}

		reactionBubble.Visible = false;

		// Usuń auto-test, bo w multiplayerze miesza w debugowaniu.
		// Jeśli chcesz, zostaw tylko na Debug build:
		// if (OS.IsDebugBuild()) ShowReaction("Test reakcji", 2.0f);
	}


	public void ShowReaction(string text, float seconds = 2.5f)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}

		// anuluj poprzedni timer (jeśli był)
		if (hideTimer != null)
		{
			hideTimer.Timeout -= OnHideTimerTimeout;
			hideTimer = null;
		}

		reactionLabel.Text = text;
		reactionBubble.Visible = true;

		hideTimer = GetTree().CreateTimer(seconds);
		hideTimer.Timeout += OnHideTimerTimeout;
	}

	private void OnHideTimerTimeout()
	{
		reactionBubble.Visible = false;

		if (hideTimer != null)
		{
			hideTimer.Timeout -= OnHideTimerTimeout;
			hideTimer = null;
		}
	}
}
