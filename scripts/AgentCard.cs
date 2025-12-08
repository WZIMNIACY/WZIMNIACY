using Godot;

public partial class AgentCard : PanelContainer
{
	private bool selected = false;
	[Export] private Button selectButton;
	[Export] private CardMenager cardMenager;
	
	[Signal] public delegate void CardConfirmedEventHandler(AgentCard card);
	
	// Called when the node enters the scene tree for the first time.
	
	public override void _Ready()
	{
		base._Ready();
		AddToGroup("cards");
		MouseFilter = MouseFilterEnum.Pass;
		SetProcessInput(true);
	}

	public override void _Process(double delta)
    {
        base._Process(delta);
    }
	
	public override void _GuiInput(InputEvent @event)
	{
		base._GuiInput(@event);
		if (@event is InputEventMouseButton mouseEvent &&
			mouseEvent.Pressed &&
			mouseEvent.ButtonIndex == MouseButton.Left)
		{
			ToggleSelected();
		}
	}
	
	public void Unselect()
	{
		selected = false;
		selectButton.Visible = false;
	}
	
	public void ToggleSelected()
	{
		selected = !selected;
		selectButton.Visible = selected;
	}
	
	public void OnSelectButtonPressed()
	{
		EmitSignal(SignalName.CardConfirmed, this);
	}
}
