using Godot;
public partial class PlayerListContainer : PanelContainer
{
	[Export] VBoxContainer playerListVBox;
	private MainGame mainGame;

	public enum PlayerTeam { None, Blue, Red }
	[Export] public PlayerTeam team;
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
		foreach(var member in mainGame.PlayersByIndex)
		{
			if((member.Value.team.ToString() == team.ToString()))
			{
				foreach (Node child in playerListVBox.GetChildren())
				{
					if (child is RichTextLabel label && label.Text == "")
					{
						label.Text = member.Value.name;
						break;
					}
				}
			}

		}
		
	}
}
