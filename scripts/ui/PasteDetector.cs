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
    /// Minimalna liczba znaków wprowadzonych jednocześnie aby uznać za paste
    /// </summary>
    [Export]
    public int MinPasteLength { get; set; } = 3;

    /// <summary>
    /// Maksymalny czas (w sekundach) pomiędzy zmianami tekstu aby uznać za paste
    /// </summary>
    [Export]
    public float MaxPasteTime { get; set; } = 0.1f;

    private string previousText = "";
    private double lastChangeTime = 0;
    private Action<string> onPasteCallback;

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
    /// Rejestruje callback do wykonania po wykryciu wklejenia
    /// </summary>
    /// <param name="callback">Funkcja przyjmująca wklejony tekst jako argument</param>
    public void RegisterPasteCallback(Action<string> callback)
    {
        onPasteCallback = callback;
    }

    /// <summary>
    /// Wywoływane gdy tekst w polu się zmienia
    /// </summary>
    private void OnTextChanged(string newText)
    {
        double currentTime = Time.GetTicksMsec() / 1000.0;

        // Jeśli to pierwsza zmiana (pole było puste), zresetuj czas
        if (string.IsNullOrEmpty(previousText))
        {
            lastChangeTime = currentTime;
        }

        double timeDiff = currentTime - lastChangeTime;

        // Oblicz różnicę w długości tekstu
        int lengthDiff = newText.Length - previousText.Length;


        // Wykryj paste
        bool isPaste = lengthDiff >= MinPasteLength && timeDiff < MaxPasteTime;

        if (isPaste)
        {
            // Wywołaj callback z całym nowym tekstem
            onPasteCallback?.Invoke(newText);
        }

        previousText = newText;
        lastChangeTime = currentTime;
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        if (Target != null)
        {
            Target.TextChanged -= OnTextChanged;
        }
    }
}
