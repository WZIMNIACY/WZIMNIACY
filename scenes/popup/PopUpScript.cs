using Godot;
using System;

public partial class PopupSystem : CanvasLayer
{
	[Export] public Control ContentContainer;
	[Export] public Label MessageLabel;       // <--- NOWE: Tutaj podepniemy tekst wiadomości
	[Export] public Button BtnConfirm;
	[Export] public Button BtnCancel;

	public override void _Ready()
	{
		// Ukrywamy okno na start gry
		Visible = false;

		if (BtnConfirm != null)
			BtnConfirm.Pressed += OnConfirmPressed;
		
		if (BtnCancel != null)
			BtnCancel.Pressed += OnCancelPressed;
	}

	// Tę funkcję będziesz wywoływał z innych miejsc w grze!
	public void ShowError(string errorText)
	{
		// 1. Ustawiamy tekst błędu
		if (MessageLabel != null)
		{
			MessageLabel.Text = errorText;
		}

		// 2. Pokazujemy warstwę
		Visible = true;
		
		// 3. Resetujemy ustawienia przed animacją
		if (ContentContainer != null)
		{
			ContentContainer.Scale = new Vector2(0.7f, 0.7f);
			ContentContainer.Modulate = new Color(1, 1, 1, 0); // Przezroczysty
			
			// 4. Animacja "wyskakiwania"
			Tween tween = CreateTween();
			tween.SetParallel(true);
			
			tween.TweenProperty(ContentContainer, "scale", new Vector2(1.0f, 1.0f), 0.4f)
				.SetTrans(Tween.TransitionType.Back)
				.SetEase(Tween.EaseType.Out); 
			
			tween.TweenProperty(ContentContainer, "modulate:a", 1.0f, 0.3f);
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
		// Opcjonalnie: Animacja zamykania (zmniejszanie)
		if (ContentContainer != null)
		{
			Tween tween = CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(ContentContainer, "scale", new Vector2(0.8f, 0.8f), 0.2f);
			tween.TweenProperty(ContentContainer, "modulate:a", 0.0f, 0.2f);
			// Po zakończeniu animacji ukryj całkowicie
			tween.Finished += () => Visible = false;
		}
		else
		{
			Visible = false;
		}
	}
}
