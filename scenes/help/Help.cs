using Godot;

/// <summary>
/// Controls the Help screen functionality, primarily handling navigation back to the previous menu.
/// </summary>
public partial class Help : Control
{
	/// <summary>
	/// Reference to the back button found in the scene tree.
	/// </summary>
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

	/// <summary>
	/// Handles the Pressed event of the BackButton.
	/// Hides the Help screen, effectively returning to the underlying MainMenu.
	/// </summary>
	private void OnBackButtonPressed()
	{
		// ZMIANA: Nie ładujemy nowej sceny, tylko się ukrywamy.
		// Dzięki temu MainMenu pod spodem nadal tam jest.
		this.Visible = false;
	}
}
