using Godot;

public partial class MainGame : Control
{
    [Export] Panel menuPanel;
    [Export] public RightPanel gameRightPanel;
    [Export] public CaptainInput gameInputPanel;

    private bool isBlueTurn = true;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
	{
        base._Ready();
        menuPanel = GetNode<Panel>("MenuPanel");
        menuPanel.Visible = false;

        if (gameInputPanel != null)
        {
            gameInputPanel.HintGiven += OnCaptainHintReceived;
            StartCaptainPhase();
        }
        else
        {
            GD.PrintErr("Error");
        }
    }

    private void StartCaptainPhase()
    {
        GD.Print($"Początek tury {(isBlueTurn ? "BLUE" : "RED")}");
        if(gameInputPanel != null)
        {
            gameInputPanel.SetupTurn(isBlueTurn);
        }
    }

    private void OnCaptainHintReceived(string word, int number)
    {
        GD.Print($"{word} [{number}]");
        if (gameRightPanel != null)
        {
            gameRightPanel.UpdateHintDisplay(word, number, isBlueTurn);
        }
    }

    public void OnSkipTurnPressed()
    {
        GD.Print("Koniec tury");
        if(gameRightPanel != null)
            gameRightPanel.CommitToHistory();
        isBlueTurn = !isBlueTurn;
        StartCaptainPhase();
    }

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
        base._Process(delta);
    }

    public void OnMenuButtonPressed()
    {
        GD.Print("Menu button pressed");
        menuPanel.Visible = true;
    }

    public void OnMenuBackgroundButtonDown()
    {
        GD.Print("MenuBackgroundButton pressed...");
        menuPanel.Visible = false;
    }

    public void OnQuitButtonPressed()
    {
        GD.Print("QuitButton pressed...");
    }

    public void OnPauseButtonPressed()
    {
        GD.Print("PauseButton pressed...");
    }

    public void OnSettingsButtonPressed()
    {
        GD.Print("SettingsButton pressed...");
    }

    public void OnHelpButtonPressed()
    {
        GD.Print("HelpButton pressed...");
    }

    public void OnResumeButtonPressed()
    {
        GD.Print("ResumeButton pressed...");
        menuPanel.Visible = false;
    }
}
