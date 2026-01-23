using Godot;

/// <summary>
/// Handles the intro sequence playback and transitions to the main menu.
/// </summary>
public partial class Intro : Control
{
    /// <summary>
    /// Reference to the VideoStreamPlayer node for playing the intro video.
    /// </summary>
    private VideoStreamPlayer videoPlayer;

    public override void _Ready()
    {
        base._Ready();

        videoPlayer = GetNode<VideoStreamPlayer>("VideoStreamPlayer");
        videoPlayer.Finished += OnVideoFinished;

        GD.Print("🎬 Playing intro video...");
    }

    /// <summary>
    /// Called when the video player finishes playing the video.
    /// Logs the completion and transitions to the main menu.
    /// </summary>
    private void OnVideoFinished()
    {
        GD.Print("✅ Intro finished, loading main menu...");
        CallDeferred(nameof(ChangeToMainMenu));
    }

    /// <summary>
    /// Changes the current scene to the main menu scene.
    /// </summary>
    private void ChangeToMainMenu()
    {
        GetTree().ChangeSceneToFile("res://scenes/menu/main.tscn");
    }

    public override void _Input(InputEvent @event)
    {
        base._Input(@event);

        // Allow skipping intro with Space, Enter, Escape or mouse click
        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            if (keyEvent.Keycode == Key.Space || keyEvent.Keycode == Key.Enter || keyEvent.Keycode == Key.Escape)
            {
                SkipIntro();
            }
        }
        else if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
        {
            SkipIntro();
        }
    }

    /// <summary>
    /// Skips the intro video sequence.
    /// Logs the skip action and transitions to the main menu.
    /// </summary>
    private void SkipIntro()
    {
        GD.Print("⏭️ Skipping intro...");
        CallDeferred(nameof(ChangeToMainMenu));
    }
}
