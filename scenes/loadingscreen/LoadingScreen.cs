using Godot;

public partial class LoadingScreen : Control
{
    [Export] NodePath potatoPath;
    [Export] NodePath quitToMenuButtonPath;
    [Export] NodePath mainGamePath;
    Sprite2D potato;
    Button quitToMenuButton;
    MainGame mainGame;

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

    public void ShowLoading()
	{
        showQuitButtonTimer.Start();
        quitToMenuButton.Visible = false;
        Visible = true;
		MouseFilter = MouseFilterEnum.Stop;
	}

	public void HideLoading()
	{
        showQuitButtonTimer.Stop();
        quitToMenuButton.Visible = false;
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
	}

    public void OnQuitButtonPressed()
    {
        mainGame.OnQuitButtonPressed();
    }
}
