using Godot;
using System;

/// <summary>
/// Obsługuje wykrywanie wklejania tekstu (paste) w polach LineEdit
/// i wykonuje określoną akcję po wklejeniu.
/// </summary>
/// <summary>
/// Wykrywa wklejanie tekstu w polu <see cref="LineEdit"/> na podstawie szybkości i długości zmian oraz skrótu Ctrl/Cmd+V;
/// wywołuje zarejestrowany callback (np. <see cref="LobbySearchMenu.OnLobbyIdPasted"/>).
/// </summary>
/// <remarks>
/// Wymaga ustawienia <see cref="Target"/> (zazwyczaj w wątku głównym Godota); klasa nie jest thread-safe.
/// </remarks>
public partial class PasteDetector : Node
{
    /// <summary>
    /// Pole tekstowe monitorowane pod kątem wklejania.
    /// </summary>
    /// <value>Referencja do <see cref="LineEdit"/>; wymagane do działania.</value>
    [Export]
    public LineEdit Target { get; set; }

    private Action<string> onPasteCallback;

    public override void _Input(InputEvent @event)
    {
        base._Input(@event);

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            bool isPasteShortcut = keyEvent.Keycode == Key.V &&
                                   (keyEvent.CtrlPressed || keyEvent.MetaPressed);

            if (isPasteShortcut && Target != null && Target.HasFocus())
            {
                // Opóźnij wykonanie callbacku do następnej klatki
                GetTree().CreateTimer(0.05).Timeout += () =>
                {
                    if (Target != null)
                    {
                        onPasteCallback?.Invoke(Target.Text);
                    }
                };
            }
        }
    }

    /// <summary>
    /// Rejestruje callback do wykonania po wykryciu wklejenia.
    /// </summary>
    /// <param name="callback">Funkcja przyjmująca wklejony tekst jako argument.</param>
    /// <seealso cref="OnTextChanged"/>
    public void RegisterPasteCallback(Action<string> callback)
    {
        onPasteCallback = callback;
    }
}
