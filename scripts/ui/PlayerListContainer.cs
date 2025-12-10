using Godot;
public partial class PlayerListContainer : PanelContainer
{
	[Export] VBoxContainer playerListVBox;
	private EOSManager eosManager;
	
	public enum PlayerTeam { None, Blue, Red }
	[Export] public PlayerTeam Team;
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
			if((member["team"].ToString() == Team.ToString()))
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
