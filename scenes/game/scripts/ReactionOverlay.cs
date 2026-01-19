using Godot;

public partial class ReactionOverlay : Control
{
	[Export] private Control reactionBubble;
	[Export] private Label reactionLabel;

	private SceneTreeTimer hideTimer;

	public override void _Ready()
	{
		if (reactionBubble == null || reactionLabel == null)
		{
			GD.PrintErr("[ReactionOverlay] CRITICAL: reactionBubble or reactionLabel not assigned in Inspector. Overlay disabled.");
			return;
		}

		reactionBubble.Visible = false;
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
