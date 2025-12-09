using Godot;

public partial class CaptainInput : Control
{
	[Signal]
	public delegate void HintGivenEventHandler(string word, int number);
	[Export] public LineEdit wordInput;
	[Export] public SpinBox numberInput;
	[Export] public Button sendButton;

	private Color blueTeamColor = new Color("5AD2C8FF");
	private Color redTeamColor = new Color("E65050FF");

    public override void _Ready()
    {
		base._Ready();
		
        if(sendButton != null)
			sendButton.Pressed += OnSendPressed;
		if (wordInput != null)
			wordInput.TextChanged += OnTextChanged;
    }

	private void OnTextChanged(string newText)
    {
        wordInput.Modulate = new Color(1, 1, 1);
    }

	public void SetupTurn(bool isBlueTeam)
	{
		this.Visible = true;

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

	private void ShowError(string message)
    {
        GD.Print($"Błąd: {message}");
        wordInput.Modulate = new Color(1, 0.3f, 0.3f); 
    }
}
