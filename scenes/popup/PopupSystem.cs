using Godot;
using System;

public partial class PopupSystem : CanvasLayer
{
	// --- UI ELEMENTS ---
	[Export] private Control contentContainer;
	[Export] private Label headerLabel;
	[Export] private Label messageLabel;
	[Export] private Button btnConfirm;
	[Export] private Button btnCancel;

	private Action onConfirmCallback;
	private Action onCancelCallback;

	public override void _Ready()
	{
		Visible = false;

		if (btnConfirm != null)
			btnConfirm.Pressed += OnConfirmPressed;

		if (btnCancel != null)
			btnCancel.Pressed += OnCancelPressed;
	}

	public override void _Input(InputEvent @event)
	{
		// Obsługa ESC - zamyka popup najbezpieczniejszą opcją
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode == Key.Escape && Visible)
			{
				// Jeśli jest przycisk Cancel i jest widoczny - kliknij Cancel (bezpieczniejsze)
				// Jeśli nie ma Cancel - kliknij OK/Confirm
				if (btnCancel != null && btnCancel.Visible)
				{
					OnCancelPressed();
				}
				else if (btnConfirm != null)
				{
					OnConfirmPressed();
				}

				GetViewport().SetInputAsHandled();
			}
		}
	}

	/// <summary>
	/// Wyświetla prosty dialog z tylko przyciskiem OK
	/// </summary>
	public void ShowMessage(string title, string message, Action onConfirm = null)
	{
		SetupDialog(title, message, "OK", null, onConfirm, null);
		if (btnCancel != null)
			btnCancel.Visible = false;
	}

	/// <summary>
	/// Wyświetla dialog z przyciskami Potwierdź/Anuluj
	/// </summary>
	public void ShowConfirmation(string title, string message, string confirmText = "POTWIERDŹ", string cancelText = "ANULUJ", Action onConfirm = null, Action onCancel = null)
	{
		SetupDialog(title, message, confirmText, cancelText, onConfirm, onCancel);
		if (btnCancel != null)
			btnCancel.Visible = true;
	}

	/// <summary>
	/// Wyświetla błąd (alias dla ShowMessage)
	/// </summary>
	public void ShowError(string errorText, Action onConfirm = null)
	{
		ShowMessage("★ BŁĄD ★", errorText, onConfirm);
	}

	private void SetupDialog(string title, string message, string confirmText, string cancelText, Action onConfirm, Action onCancel)
	{
		// Ustaw tytuł
		if (headerLabel != null)
		{
			headerLabel.Text = title;
		}

		// Ustaw wiadomość
		if (messageLabel != null)
		{
			messageLabel.Text = message;
		}

		// Ustaw teksty przycisków
		if (btnConfirm != null && !string.IsNullOrEmpty(confirmText))
		{
			btnConfirm.Text = confirmText;
		}

		if (btnCancel != null && !string.IsNullOrEmpty(cancelText))
		{
			btnCancel.Text = cancelText;
		}

		// Zapisz callbacki
		onConfirmCallback = onConfirm;
		onCancelCallback = onCancel;

		// Pokazujemy warstwę z animacją
		Visible = true;

		// Resetujemy ustawienia przed animacją
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
		onConfirmCallback?.Invoke();
		HidePopup();
	}

	private void OnCancelPressed()
	{
		onCancelCallback?.Invoke();
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
			tween.Finished += () =>
			{
				Visible = false;
				// Wyczyść callbacki po zamknięciu
				onConfirmCallback = null;
				onCancelCallback = null;
			};
		}
		else
		{
			Visible = false;
			onConfirmCallback = null;
			onCancelCallback = null;
		}
	}
}
