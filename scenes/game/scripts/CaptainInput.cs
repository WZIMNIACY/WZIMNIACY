using Godot;

/// <summary>
/// Manages the input interface for the Captain, allowing them to provide a hint word and a number.
/// </summary>
public partial class CaptainInput : Control
{
	/// <summary>
	/// Signal emitted when the captain submits a hint.
	/// </summary>
	/// <param name="word">The hint word provided by the captain.</param>
	/// <param name="number">The number associated with the hint.</param>
	[Signal]
	public delegate void HintGivenEventHandler(string word, int number);

	/// <summary>
	/// Input field for the hint word.
	/// </summary>
	[Export] public LineEdit wordInput;

	/// <summary>
	/// Input field for the number associated with the hint.
	/// </summary>
	[Export] public SpinBox numberInput;

	/// <summary>
	/// Button to confirm and send the hint.
	/// </summary>
	[Export] public Button sendButton;

	/// <summary>
	/// Determines the initial visibility of the control.
	/// </summary>
	[Export] public bool visible = false;

	/// <summary>
	/// Color for the Blue team's interface elements.
	/// </summary>
	private Color blueTeamColor = new Color("5AD2C8FF");

	/// <summary>
	/// Color for the Red team's interface elements.
	/// </summary>
	private Color redTeamColor = new Color("E65050FF");

	public override void _Ready()
	{
		base._Ready();
		Visible = visible;

		if(sendButton != null)
			sendButton.Pressed += OnSendPressed;
		if (wordInput != null)
			wordInput.TextChanged += OnTextChanged;
	}

	/// <summary>
	/// Handles text changes in the word input field.
	/// Resets the modulate color to white when the user types.
	/// </summary>
	/// <param name="newText">The new text in the input field.</param>
	private void OnTextChanged(string newText)
	{
		wordInput.Modulate = new Color(1, 1, 1);
	}

	/// <summary>
	/// Sets up the input interface for the current turn.
	/// Resets input fields and sets the send button color based on the current team.
	/// </summary>
	/// <param name="isBlueTeam">True if it is the Blue team's turn, false for Red team.</param>
	public void SetupTurn(bool isBlueTeam)
	{
		this.Visible = visible;

		if(wordInput != null)
		{
			wordInput.Text = "";
			wordInput.Modulate = new Color(1, 1, 1);
			wordInput.PlaceholderText = "Słowo:";
		}
		if(numberInput != null)
			numberInput.Value = 1;
		if(sendButton != null)
		{
			sendButton.Modulate = isBlueTeam ? blueTeamColor : redTeamColor;
		}
	}

	/// <summary>
	/// Handles the press event of the send button.
	/// Validates the input and emits the <see cref="HintGiven"/> signal if valid.
	/// </summary>
	private void OnSendPressed()
	{
		if(wordInput == null) return;

		string text = wordInput.Text.Trim();
		int number = (int)numberInput.Value;

		if (text.Contains(" "))
		{
			ShowError("Tylko jedno słowo!");
			return;
		}

		if(!string.IsNullOrEmpty(text))
		{
			EmitSignal(SignalName.HintGiven, text, number);

			this.Visible = false;
		}
	}

	/// <summary>
	/// Displays an error indication, changing the input field color.
	/// </summary>
	/// <param name="message">The error message to log.</param>
	private void ShowError(string message)
	{
		GD.Print($"Błąd: {message}");
		wordInput.Modulate = new Color(1, 0.3f, 0.3f);
	}
}
