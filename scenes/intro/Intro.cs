using Godot;

public partial class Intro : Control
{
    private VideoStreamPlayer videoPlayer;

    public override void _Ready()
    {
        base._Ready();

        videoPlayer = GetNode<VideoStreamPlayer>("VideoStreamPlayer");
        videoPlayer.Finished += OnVideoFinished;

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

    private void SkipIntro()
    {
        GD.Print("⏭️ Skipping intro...");
        CallDeferred(nameof(ChangeToMainMenu));
    }
}
