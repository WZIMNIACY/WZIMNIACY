using Godot;
public partial class PlayerListContainer : PanelContainer
{
	[Export] VBoxContainer playerListVBox;
	private EOSManager eosManager;
	
	public enum PlayerTeam { None, Blue, Red }
<<<<<<< HEAD
<<<<<<< HEAD
	[Export] public PlayerTeam team;
=======
	[Export] public PlayerTeam Team;
>>>>>>> 4076424 (Wyswietlanie graczy na liscie)
=======
	[Export] public PlayerTeam team;
>>>>>>> 5ef2641 (Wyswietlanie graczy na liscie)
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		base._Ready();
		eosManager = GetNode<EOSManager>("/root/EOSManager");

		GD.Print("EOSManager found: " + (eosManager != null));
		SetPlayerListVBox();
		
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		base._Process(delta);
	}

	public void SetPlayerListVBox()
	{
		foreach (var member in eosManager.GetCurrentLobbyMembers())
		{
<<<<<<< HEAD
<<<<<<< HEAD
			if((member["team"].ToString() == team.ToString()))
=======
			if((member["team"].ToString() == Team.ToString()))
>>>>>>> 4076424 (Wyswietlanie graczy na liscie)
=======
			if((member["team"].ToString() == team.ToString()))
>>>>>>> 5ef2641 (Wyswietlanie graczy na liscie)
			{
				foreach (Node child in playerListVBox.GetChildren())
				{
					if (child is RichTextLabel label && label.Text == "")
					{
						label.Text = member["displayName"].ToString();
						break;
					}
				}
			}

		}
		
		int i = 0;
		foreach (Node child in playerListVBox.GetChildren())
		{
			if (child is RichTextLabel label && label.Text == "")
			{
				label.Text = "Gracz " + i.ToString();
				i++;
			}
		}
		
	}
}
