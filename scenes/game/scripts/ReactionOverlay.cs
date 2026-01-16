using Godot;
using System;

public partial class ReactionOverlay : CanvasLayer
{
	private Control reactionBubble;
	private Label reactionLabel;

	private SceneTreeTimer hideTimer;

	public override void _Ready()
	{
		reactionBubble = GetNode<Control>("ReactionBubble");
		reactionLabel = GetNode<Label>("ReactionBubble/ReactionLabel");

		reactionBubble.Visible = false;
		ShowReaction("Test reakcji", 2.0f);

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
