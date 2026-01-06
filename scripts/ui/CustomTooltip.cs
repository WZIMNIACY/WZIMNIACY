using Godot;

/// <summary>
/// Prosty tooltip z opóźnionym wyświetlaniem, podążający za kursorem.
/// </summary>
/// <remarks>
/// Wymaga dziecka typu <see cref="Label"/> o nazwie "Label"; działa w wątku głównym Godota (nie thread-safe).
/// </remarks>
public partial class CustomTooltip : PanelContainer
{
    /// <summary>Label przechowujący tekst tooltipa.</summary>
    private Label label;
    /// <summary>Timer opóźniający wyświetlenie tooltipa.</summary>
    private Timer showTimer;

    private const float ShowDelay = 0.2f; // 200ms opóźnienia
    private const int MouseOffsetX = 10;
    private const int MouseOffsetY = 10;

    /// <summary>
    /// Inicjalizuje label, timer oraz początkowy stan tooltipa.
    /// </summary>
    /// <seealso cref="OnShowTimerTimeout"/>
    /// <seealso cref="UpdateSize"/>
    public override void _Ready()
    {
        base._Ready();

        // Pobierz label z dziecka
        label = GetNode<Label>("Label");

        // Stwórz timer
        showTimer = new Timer();
        showTimer.WaitTime = ShowDelay;
        showTimer.OneShot = true;
        showTimer.Timeout += OnShowTimerTimeout;
        AddChild(showTimer);

        // Początkowa konfiguracja
        Visible = false;
        ZIndex = 100;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>
    /// Aktualizuje pozycję tooltipa gdy jest widoczny.
    /// </summary>
    /// <param name="delta">Czas od ostatniej klatki.</param>
    /// <seealso cref="UpdatePosition"/>
    public override void _Process(double delta)
    {
        base._Process(delta);

        // Aktualizuj pozycję tooltipa gdy jest widoczny
        if (Visible)
        {
            UpdatePosition();
        }
    }

    /// <summary>
    /// Pokazuje tooltip z podanym tekstem po krótkim opóźnieniu.
    /// </summary>
    /// <param name="text">Treść wyświetlana w tooltipie.</param>
    /// <seealso cref="UpdateSize"/>
    /// <seealso cref="OnShowTimerTimeout"/>
    public void Show(string text)
    {
        if (label != null)
        {
            label.Text = text;
            // Wymuś aktualizację rozmiaru
            CallDeferred(nameof(UpdateSize));
        }

        if (!showTimer.IsStopped())
        {
            showTimer.Stop();
        }

        showTimer.Start();
    }

    /// <summary>
    /// Ukrywa tooltip
    /// </summary>
    /// <seealso cref="Show"/>
    public new void Hide()
    {
        Visible = false;

        if (showTimer != null && !showTimer.IsStopped())
        {
            showTimer.Stop();
        }
    }

    /// <summary>
    /// Callback gdy timer się skończy
    /// </summary>
    /// <seealso cref="UpdatePosition"/>
    private void OnShowTimerTimeout()
    {
        Visible = true;
        UpdatePosition();
    }

    /// <summary>
    /// Aktualizuje rozmiar tooltipa do zawartości
    /// </summary>
    private void UpdateSize()
    {
        // Resetuj rozmiar
        Size = Vector2.Zero;
        CustomMinimumSize = Vector2.Zero;

        // Wymuś przeliczenie layoutu
        if (label != null)
        {
            label.Size = Vector2.Zero;
        }

        ResetSize();
    }

    /// <summary>
    /// Aktualizuje pozycję tooltipa przy kursorze
    /// </summary>
    private void UpdatePosition()
    {
        var mousePos = GetGlobalMousePosition();
        Position = mousePos + new Vector2(MouseOffsetX, MouseOffsetY);

        // Upewnij się że tooltip nie wychodzi poza ekran
        var tooltipSize = Size;
        var screenSize = GetViewportRect().Size;

        Vector2 newPosition = Position;

        if (Position.X + tooltipSize.X > screenSize.X)
        {
            newPosition.X = screenSize.X - tooltipSize.X - 10;
        }
        if (Position.Y + tooltipSize.Y > screenSize.Y)
        {
            newPosition.Y = mousePos.Y - tooltipSize.Y - 10;
        }
        Position = newPosition;
    }
}
