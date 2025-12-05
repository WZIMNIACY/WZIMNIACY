using Godot;
using System;

public enum Team
{
    Blue,
    Red
}

public partial class MainGame : Control
{
    Panel menuPanel;
    Label pointsLabelBlue;
    Label pointsLabelRed;
    ColorRect turnDiodeBlue;
    ColorRect turnDiodeRed;
    PanelContainer teamListBlue;
    PanelContainer teamListRed;
    public const bool IsHost = true; // temp value
    private int pointsBlue;
    public int PointsBlue
    {
        get => pointsBlue;
    }
    private int pointsRed;
    public int PointsRed
    {
        get => pointsRed;
    }
    Team startingTeam;
    Team currentTurn;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        base._Ready();
        menuPanel = GetNode<Panel>("MenuPanel");
        pointsLabelBlue = GetNode<Label>("%BlueScoreContainer/VBoxContainer/Label");
        pointsLabelRed = GetNode<Label>("%RedScoreContainer/VBoxContainer/Label");
        turnDiodeBlue = GetNode<ColorRect>("%BlueScoreContainer/VBoxContainer/Control/ColorRect");
        turnDiodeRed = GetNode<ColorRect>("%RedScoreContainer/VBoxContainer/Control/ColorRect");
        teamListBlue = GetNode<PanelContainer>("%BluePlayerList/VBoxContainer/PanelContainer");
        teamListRed = GetNode<PanelContainer>("%RedPlayerList/VBoxContainer/PanelContainer");

        // Hide menu panel at start
        menuPanel.Visible = false;

        // Choosing starting team
        if (IsHost)
        {
            // Choose starting team randomly
            startingTeam = (Team)Random.Shared.Next(0, 2);

            // TODO: send starting team to clients
        }
        else
        {
            // TODO: wait to receive starting team from host
        }

        // Assing initianl points and turn
        if (startingTeam == Team.Blue)
        {
            currentTurn = Team.Blue;
            pointsBlue = 9;
            pointsRed = 8;
            SetTurnBlue();
        }
        else
        {
            currentTurn = Team.Red;
            pointsBlue = 8;
            pointsRed = 9;
            SetTurnRed();
        }
        UpdatePointsDisplay();
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

    private void UpdatePointsDisplay()
    {
        string textBlue = "Karty niebieskich: "; // temp value, czekam na lepsza propozycje od UI teamu
        string textRed = "Karty czerwonych: "; // same
        pointsLabelBlue.Text = textBlue + pointsBlue.ToString();
        pointsLabelRed.Text = textRed + pointsRed.ToString();
    }

    public void RemovePointBlue()
    {
        GD.Print("Point removed from team blue...");
        pointsBlue--;
        UpdatePointsDisplay();
        if (pointsBlue == 0)
            EndGame(Team.Blue);
    }

    public void RemovePointRed()
    {
        GD.Print("Point removed from team red...");
        pointsRed--;
        UpdatePointsDisplay();
        if (pointsRed == 0)
            EndGame(Team.Red);
    }

    public void TurnChange()
    {
        if (currentTurn == Team.Blue)
            SetTurnRed();
        else
            SetTurnBlue();
    }

    public void SetTurnBlue()
    {
        GD.Print("Turn blue...");
        currentTurn = Team.Blue;
        turnDiodeBlue.Visible = true;
        turnDiodeRed.Visible = false;
        teamListBlue.Modulate = new Color(2.8f, 2.8f, 2.8f, 1f);
        teamListRed.Modulate = new Color(1f, 1f, 1f, 1f);
    }

    public void SetTurnRed()
    {
        GD.Print("Turn red...");
        currentTurn = Team.Red;
        turnDiodeBlue.Visible = false;
        turnDiodeRed.Visible = true;
        teamListBlue.Modulate = new Color(1f, 1f, 1f, 1f);
        teamListRed.Modulate = new Color(2.8f, 2.8f, 2.8f, 1f);
    }

    public void EndGame(Team winner)
    {
    }
}
