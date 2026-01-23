using Godot;
using System;

/// <summary>
/// Wykrywa wklejanie tekstu w polu <see cref="LineEdit"/> na podstawie skrótu Ctrl/Cmd+V oraz zmian w polu;
/// wywołuje zarejestrowany callback (np. <see cref="LobbySearchMenu.OnLobbyIdPasted"/>).
/// </summary>
/// <remarks>
/// Wymaga ustawienia <see cref="Target"/>
/// </remarks>
public partial class PasteDetector : Node
{
    /// <summary>
    /// Pole tekstowe monitorowane pod kątem wklejania.
    /// </summary>
    /// <value>Referencja do <see cref="LineEdit"/>; wymagane do działania.</value>
    [Export]
    public LineEdit Target { get; set; }

    /// <summary>Callback wywoływany po wykryciu wklejenia.</summary>
    private Action<string> onPasteCallback;

    /// <summary>
    /// Nasłuchuje zdarzeń klawiatury i uruchamia callback po wykryciu wklejenia do aktywnego pola.
    /// </summary>
    /// <param name="@event">Zdarzenie wejściowe przekazane przez Godot.</param>
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
    public void RegisterPasteCallback(Action<string> callback)
    {
        onPasteCallback = callback;
    }
}
