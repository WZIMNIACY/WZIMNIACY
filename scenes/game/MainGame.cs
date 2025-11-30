using Godot;
using System;

public enum Team
{
    Red,
    Blue
}

public partial class MainGame : Control
{
    [Export] Panel menuPanel;
    [Export] Label pointsBlueLabel;
    [Export] Label pointsRedLabel;
    public const bool HOST = true; // temp value
    public int pointsBlue;
    public int pointsRed;
    Team startingTeam;
    Team currentTurn;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        base._Ready();
        menuPanel = GetNode<Panel>("MenuPanel");
        pointsBlueLabel = GetNode<Label>("Panel/HBoxContainer/MiddlePanel/VMiddlePanel/TopBar/BlueScoreContainer/VBoxContainer/Label");
        pointsRedLabel = GetNode<Label>("Panel/HBoxContainer/MiddlePanel/VMiddlePanel/TopBar/RedScoreContainer/VBoxContainer/Label");

        // Hide menu panel at start
        menuPanel.Visible = false;

        // Choosing starting team
        if (HOST)
        {
            // Choose starting team randomly
            var values = Enum.GetValues(typeof(Team));
            Team startingTeam = (Team)values.GetValue(Random.Shared.Next(values.Length));

            // Send starting team to clients
        }
        else
        {
            // Wait to receive starting team from host
        }

        // Assing initianl points and turn
        if (startingTeam == Team.Blue)
        {
            currentTurn = Team.Blue;
            pointsBlue = 9;
            pointsRed = 8;
        }
        else
        {
            currentTurn = Team.Red;
            pointsBlue = 8;
            pointsRed = 9;
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
        pointsBlueLabel.Text = textBlue + pointsBlue.ToString();
        pointsRedLabel.Text = textRed + pointsRed.ToString();
    }

    public void RemovePointBlue()
    {
        pointsBlue--;
        UpdatePointsDisplay();
        if (pointsBlue == 0)
            EndGame(Team.Blue);
    }

    public void RemovePointRed()
    {
        pointsRed--;
        UpdatePointsDisplay();
        if (pointsRed == 0)
            EndGame(Team.Red);
    }

    public void TurnChange()
    {
        if (currentTurn == Team.Blue)
        {
            currentTurn = Team.Red;
        }
        else
        {
            currentTurn = Team.Blue;
        }
    }

    public void EndGame(Team winner)
    {
    }
}
