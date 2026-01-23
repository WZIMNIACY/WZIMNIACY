using Godot;

/// <summary>
/// Controls the display of a score label and a status diode.
/// </summary>
public partial class ScoreContainer : PanelContainer
{
    /// <summary>
    /// The label used to display the score text.
    /// </summary>
    [Export] Label scoreLabel;

    /// <summary>
    /// A visual indicator (diode) that can be toggled on or off.
    /// </summary>
    [Export] ColorRect diode;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
	{
        base._Ready();
    }

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
        base._Process(delta);
    }

    /// <summary>
    /// Updates the text of the score label.
    /// </summary>
    /// <param name="newText">The new text to display on the label.</param>
    public void ChangeScoreText(string newText)
    {
        scoreLabel.Text = newText;
    }

    /// <summary>
    /// Turns the diode indicator on (makes it visible).
    /// </summary>
    public void SetDiodeOn()
    {
        diode.Visible = true;
    }

    /// <summary>
    /// Turns the diode indicator off (makes it invisible).
    /// </summary>
    public void SetDiodeOff()
    {
        diode.Visible = false;
    }
}
