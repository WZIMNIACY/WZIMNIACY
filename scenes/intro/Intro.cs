using Godot;

public partial class Intro : Control
{
    private VideoStreamPlayer _videoPlayer;

    public override void _Ready()
    {
        _videoPlayer = GetNode<VideoStreamPlayer>("VideoStreamPlayer");
        _videoPlayer.Finished += OnVideoFinished;

        GD.Print("🎬 Playing intro video...");
    }

    private void OnVideoFinished()
    {
        GD.Print("✅ Intro finished, loading main menu...");
        CallDeferred(nameof(ChangeToMainMenu));
    }

    private void ChangeToMainMenu()
    {
        GetTree().ChangeSceneToFile("res://scenes/menu/main.tscn");
    }

    public override void _Input(InputEvent @event)
    {
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

    private void SkipIntro()
    {
        GD.Print("⏭️ Skipping intro...");
        CallDeferred(nameof(ChangeToMainMenu));
    }
}
