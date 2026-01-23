using Godot;

/// <summary>
/// Controls the reaction overlay UI, displaying a bubble and text message above a character or object.
/// </summary>
public partial class ReactionOverlay : Control
{
	/// <summary>
	/// The control representing the visual bubble background.
	/// </summary>
	[Export] private Control reactionBubble;

	/// <summary>
	/// The label displaying the reaction text.
	/// </summary>
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

	/// <summary>
	/// Displays a reaction message for a specified duration.
	/// </summary>
	/// <param name="text">The text message to display.</param>
	/// <param name="seconds">The duration in seconds before the reaction hides automatically. Defaults to 2.5 seconds.</param>
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

	/// <summary>
	/// Callback triggered when the hide timer times out.
	/// Hides the reaction bubble and cleans up the timer.
	/// </summary>
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
