using Godot;

/// <summary>
/// Manages the display of the player list for a specific team.
/// </summary>
public partial class PlayerListContainer : PanelContainer
{
	/// <summary>
	/// The VBoxContainer that holds the player list items.
	/// </summary>
	[Export] VBoxContainer playerListVBox;

	/// <summary>
	/// The base font used for player names.
	/// </summary>
	[Export] Font baseFont;

	/// <summary>
	/// The bold font used for the local player's name to highlight it.
	/// </summary>
	[Export] Font boldFont;

	/// <summary>
	/// Reference to the MainGame instance.
	/// </summary>
	private MainGame mainGame;

	/// <summary>
	/// The team associated with this player list.
	/// </summary>
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

	/// <summary>
	/// Populates the player list VBox with players from the assigned team.
	/// Updates the UI to show player icons and names, highlighting the local player.
	/// </summary>
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

					var font = baseFont;
					if (member.Value.puid == mainGame.P2PNet.LocalPuid.ToString())
					{
						label.Text += " (TY)";
						font = boldFont;
					}
					label.AddThemeFontOverride("font", font);
					index++;
				}
			}
		}
	}
}
