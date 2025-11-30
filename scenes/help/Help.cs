using Godot;
using System;

public partial class Help : Control
{
    private Button backButton;

    public override void _Ready()
    {
        backButton = GetNode<Button>("Control/BackButton");
        if (backButton != null)
        {
            backButton.Pressed += OnBackButtonPressed;
        }
    }

    private void OnBackButtonPressed()
    {
        GD.Print("Returning to Main Menu...");
        GetTree().ChangeSceneToFile("res://Scenes/MainMenu/main.tscn");
    }
}