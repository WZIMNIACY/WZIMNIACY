using Godot;
using System;

public partial class SelectButton : Control
{
	private bool selected = false;
	[Export]
	private Button selectButton;
	private CardMenager cardMenager;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		var parent = GetParent().GetParent();
		cardMenager = parent.GetNode<CardMenager>("CardMenager");
		cardMenager.Connect(CardMenager.SignalName.UnselectCards, new Callable(this, nameof(Unselect)));
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
	
	public void OnSelectButtonPressed(){
		cardMenager.Check();
	}
}