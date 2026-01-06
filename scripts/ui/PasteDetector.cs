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
    public LineEdit Target
    {
        get => target;
        set
        {
            // Odłącz stary target jeśli był
            if (target != null)
            {
                target.TextChanged -= OnTextChanged;
            }

            target = value;

            // Podłącz nowy target
            if (target != null)
            {
                target.TextChanged += OnTextChanged;
                lastChangeTime = Time.GetTicksMsec() / 1000.0;
            }
        }
    }
    private LineEdit target;

    /// <summary>
    /// Minimalna liczba znaków dodanych na raz, by uznać zmianę za wklejenie.
    /// </summary>
    /// <value>Domyślnie 3; wartości ujemne niezalecane.</value>
    [Export]
    public int MinPasteLength { get; set; } = 3;

    /// <summary>
    /// Maksymalny czas (sekundy) między zmianami tekstu, by sklasyfikować je jako wklejenie.
    /// </summary>
    /// <value>Domyślnie 0.02 sekundy; powinno być dodatnie.</value>
    [Export]
    public float MaxPasteTime { get; set; } = 0.02f;

    /// <summary>Poprzednia wartość tekstu używana do obliczenia różnicy długości.</summary>
    private string previousText = "";
    /// <summary>Znacznik czasu (s) ostatniej zmiany tekstu.</summary>
    private double lastChangeTime = 0;
    /// <summary>Zarejestrowany callback wywoływany po wykryciu wklejenia.</summary>
    private Action<string> onPasteCallback;

    /// <summary>
    /// Podpina zdarzenia dla monitorowanego pola i resetuje licznik czasu przy starcie.
    /// </summary>
    /// <seealso cref="OnTextChanged"/>
    public override void _Ready()
    {
        base._Ready();

        if (Target != null)
        {
            Target.TextChanged += OnTextChanged;
            lastChangeTime = Time.GetTicksMsec() / 1000.0;
        }
    }

    /// <summary>
    /// Nasłuchuje skrótu wklejania (Ctrl/Cmd+V) i wyzwala callback po krótkim opóźnieniu.
    /// </summary>
    /// <param name="@event">Zdarzenie wejściowe z Godot.</param>
    /// <seealso cref="RegisterPasteCallback"/>
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

    /// <summary>
    /// Reaguje na każdą zmianę tekstu w monitorowanym polu i wykrywa wklejenie na podstawie tempa oraz długości zmiany.
    /// </summary>
    /// <param name="newText">Aktualna treść pola po zmianie.</param>
    /// <seealso cref="RegisterPasteCallback"/>
    private void OnTextChanged(string newText)
    {
        double currentTime = Time.GetTicksMsec() / 1000.0;
        double timeDiff = currentTime - lastChangeTime;

        // Oblicz różnicę w długości tekstu
        int lengthDiff = newText.Length - previousText.Length;

        // Wykryj paste
        bool isPaste = (timeDiff < MaxPasteTime && newText.Length >= MinPasteLength && lengthDiff >= -1) ||
                       lengthDiff >= MinPasteLength;

        if (isPaste)
        {
            // Wywołaj callback z całym nowym tekstem
            onPasteCallback?.Invoke(newText);
        }

        previousText = newText;
        lastChangeTime = currentTime;
    }

    /// <summary>
    /// Odłącza subskrypcję zmian tekstu przy usuwaniu węzła.
    /// </summary>
    public override void _ExitTree()
    {
        base._ExitTree();

        if (Target != null)
        {
            Target.TextChanged -= OnTextChanged;
        }
    }
}
