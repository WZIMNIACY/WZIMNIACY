using Godot;

public partial class PopupSystem : CanvasLayer
{
	// --- UI ELEMENTS ---
	[Export] private Control contentContainer;
	[Export] private Label messageLabel;
	[Export] private Button btnConfirm;
	[Export] private Button btnCancel;

	public override void _Ready()
	{
		Visible = false;

		if (btnConfirm != null)
			btnConfirm.Pressed += OnConfirmPressed;
		
		if (btnCancel != null)
			btnCancel.Pressed += OnCancelPressed;
	}

	public void ShowError(string errorText)
	{
		// 1. Ustawiamy tekst błędu
		if (messageLabel != null)
		{
			messageLabel.Text = errorText;
		}

		// 2. Pokazujemy warstwę
		Visible = true;
		
		// 3. Resetujemy ustawienia przed animacją
		if (contentContainer != null)
		{
			contentContainer.Scale = new Vector2(0.7f, 0.7f);
			contentContainer.Modulate = new Color(1, 1, 1, 0); // Przezroczysty
			
			Tween tween = CreateTween();
			tween.SetParallel(true);
			
			tween.TweenProperty(contentContainer, "scale", new Vector2(1.0f, 1.0f), 0.4f)
				.SetTrans(Tween.TransitionType.Back)
				.SetEase(Tween.EaseType.Out); 
			
			tween.TweenProperty(contentContainer, "modulate:a", 1.0f, 0.3f);
		}
	}

	private void OnConfirmPressed()
	{
		// Tutaj logika co ma się stać po kliknięciu OK
		HidePopup(); 
	}

	private void OnCancelPressed()
	{
		HidePopup();
	}
	
	private void HidePopup()
	{
		if (contentContainer != null)
		{
			Tween tween = CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(contentContainer, "scale", new Vector2(0.8f, 0.8f), 0.2f);
			tween.TweenProperty(contentContainer, "modulate:a", 0.0f, 0.2f);
			tween.Finished += () => Visible = false;
		}
		else
		{
			Visible = false;
		}
	}
}
