using Godot;

/// <summary>
/// Manages the loading screen visibility and behavior, including a rotating potato animation
/// and a failsafe quit button.
/// </summary>
public partial class LoadingScreen : Control
{
    /// <summary>
    /// Path to the rotating potato sprite node.
    /// </summary>
    [Export] NodePath potatoPath;

    /// <summary>
    /// Path to the button that returns to the menu.
    /// </summary>
    [Export] NodePath quitToMenuButtonPath;

    /// <summary>
    /// Path to the MainGame node.
    /// </summary>
    [Export] NodePath mainGamePath;

    /// <summary>
    /// Reference to the rotating potato sprite.
    /// </summary>
    Sprite2D potato;

    /// <summary>
    /// Reference to the quit button.
    /// </summary>
    Button quitToMenuButton;

    /// <summary>
    /// Reference to the MainGame instance.
    /// </summary>
    MainGame mainGame;

    /// <summary>
    /// Timer that triggers the visibility of the quit button after a delay.
    /// </summary>
    private Godot.Timer showQuitButtonTimer;

    public override void _Ready()
    {
        base._Ready();
        potato = GetNode<Sprite2D>("%LoadingScreenPotato");
        quitToMenuButton = GetNode<Button>(quitToMenuButtonPath);
        mainGame = GetNode<MainGame>(mainGamePath);

        showQuitButtonTimer = new Timer();
        showQuitButtonTimer.WaitTime = 60.0;
        showQuitButtonTimer.OneShot = true;
        showQuitButtonTimer.Autostart = false;
        showQuitButtonTimer.Timeout += () => { quitToMenuButton.Visible = true; };
        AddChild(showQuitButtonTimer);

        quitToMenuButton.Visible = false;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        potato.RotationDegrees += 270f * (float)delta;
    }

    /// <summary>
    /// Displays the loading screen, starts the failsafe timer, and blocks mouse input.
    /// </summary>
    public void ShowLoading()
	{
        showQuitButtonTimer.Start();
        quitToMenuButton.Visible = false;
        Visible = true;
		MouseFilter = MouseFilterEnum.Stop;
	}

    /// <summary>
    /// Hides the loading screen, stops the failsafe timer, and allows mouse input to pass through.
    /// </summary>
	public void HideLoading()
	{
        showQuitButtonTimer.Stop();
        quitToMenuButton.Visible = false;
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
	}

    /// <summary>
    /// Handles the event when the quit button is pressed, delegating to the MainGame.
    /// </summary>
    public void OnQuitButtonPressed()
    {
        mainGame.OnQuitButtonPressed();
    }
}
