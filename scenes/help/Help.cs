using Godot;

public partial class Help : Control
{
	[Signal]
	public delegate void BackRequestedEventHandler();

	private Button backButton;

	public override void _Ready()
	{
		// Używamy GetNodeOrNull, żeby uniknąć crasha, jeśli ścieżka się zmieni
		backButton = GetNodeOrNull<Button>("Control/BackButton");

		if (backButton != null)
		{
			backButton.Pressed += OnBackButtonPressed;
		}
		else
		{
			GD.PrintErr("❌ Help.cs: Nie znaleziono przycisku 'Control/BackButton'! Sprawdź strukturę w scenie Help.tscn.");
		}
	}

	private void OnBackButtonPressed()
	{
		EmitSignal(SignalName.BackRequested);
	}
}
