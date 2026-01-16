using Godot;
public partial class PlayerListContainer : PanelContainer
{
	[Export] VBoxContainer playerListVBox;
	private MainGame mainGame;
	private EOSManager eosManager;

	[Export] public MainGame.Team team;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		base._Ready();
		mainGame = GetNode<MainGame>("/root/Control");
		GD.Print("MainGame found: " + (mainGame != null));
		
		mainGame.GameReady += SetPlayerListVBox;
		
		eosManager = GetNode<EOSManager>("/root/EOSManager");
		GD.Print("EOSManager found: " + (eosManager != null));
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
					foreach(var eosMember in eosManager.CurrentLobbyMembers)
					{
						if(eosMember["displayName"].ToString() == member.Value.name)
						{
							int profileIcon = (int)eosMember["profileIcon"];
							string colorPrefix = eosMember["team"].ToString() == "Red" ? "red" : "blue";
							string iconPath = $"res://assets/profilePictures/Prof_{colorPrefix}_{profileIcon}.png";
							icon.Texture = GD.Load<Texture2D>(iconPath);
							break;
						}
					}
					var label = playerRow.GetChild<Label>(1);
					label.Text = member.Value.name;
					index++;
				}
			}
		}
	}
}
