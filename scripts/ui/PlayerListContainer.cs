using Godot;
public partial class PlayerListContainer : PanelContainer
{
	[Export] VBoxContainer playerListVBox;
	[Export] Font baseFont;
	[Export] Font boldFont;
	private MainGame mainGame;

	[Export] public MainGame.Team team;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		base._Ready();
		mainGame = GetNode<MainGame>("/root/Control");
		GD.Print("MainGame found: " + (mainGame != null));

		mainGame.GameReady += SetPlayerListVBox;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		base._Process(delta);
	}

	public void SetPlayerListVBox(){
		int index = 0;
		foreach(var member in mainGame.PlayersByIndex)
		{
			if((member.Value.team == team))
			{
				var child = playerListVBox.GetChildren();
				if (index < 5 && child[index] is HBoxContainer playerRow)
				{
					var icon = playerRow.GetChild<TextureRect>(0);
					icon.Texture = GD.Load<Texture2D>(member.Value.profileIconPath);
					var label = playerRow.GetChild<Label>(1);
					label.Text = member.Value.name;
					var font = member.Value.puid == mainGame.P2PNet.LocalPuid.ToString()
						? boldFont
						: baseFont;
					label.AddThemeFontOverride("font", font);
					index++;
				}
			}
		}
	}
}
