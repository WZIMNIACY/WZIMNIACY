using Godot;
using System;

public partial class SelectButton : Control
{
	private bool selected = false;
	[Export]
	private Button selectButton;
	private Mediator mediator;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		mediator = GetNode<Mediator>("/root/Control/Mediator");
		mediator.Connect(Mediator.SignalName.UnselectCards, new Callable(this, nameof(Unselect)));
		MouseFilter = MouseFilterEnum.Pass;
		SetProcessInput(true);
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
	 public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent &&
			mouseEvent.Pressed &&
			mouseEvent.ButtonIndex == MouseButton.Left)
		{
			ToggleSelected();
		}
	}
	
	public void Unselect(){
		selected = false;
		selectButton.Visible = false;
	}
	
	public void ToggleSelected(){
		selected = !selected;
		selectButton.Visible = selected;
	}
	
	public void _on_select_button_pressed(){
		mediator.Check();
	}
}
