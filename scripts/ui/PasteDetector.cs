using Godot;
using System;

/// <summary>
/// Obsługuje wykrywanie wklejania tekstu (paste) w polach LineEdit
/// i wykonuje określoną akcję po wklejeniu.
/// </summary>
public partial class PasteDetector : Node
{
    /// <summary>
    /// LineEdit który ma być monitorowany
    /// </summary>
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
    /// Rejestruje callback do wykonania po wykryciu wklejenia
    /// </summary>
    /// <param name="callback">Funkcja przyjmująca wklejony tekst jako argument</param>
    public void RegisterPasteCallback(Action<string> callback)
    {
        onPasteCallback = callback;
    }
}
