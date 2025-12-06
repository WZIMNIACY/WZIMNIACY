using Godot;

public partial class ScoreContainer : PanelContainer
{
    [Export] Label scoreLabel;
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

    public void ChangeScoreText(string newText)
    {
        scoreLabel.Text = newText;
    }

    public void SetDiodeOn()
    {
        diode.Visible = true;
    }

    public void SetDiodeOff()
    {
        diode.Visible = false;
    }
}
