using Godot;
using System;
using System.Runtime.CompilerServices;
using System.Linq;

public partial class CaptainInput : Control
{
	[Signal]
	public delegate void HintGivenEventHandler(string word, int number);
		[Export] public LineEdit WordInput;
		[Export] public SpinBox NumberInput;
		[Export] public Button SendButton;

		private Color _blueTeamColor = new Color("5AD2C8FF");
		private Color _redTeamColor = new Color("E65050FF");
    public override void _Ready()
    {
        if(SendButton != null)
			SendButton.Pressed += OnSendPressed;
		if (WordInput != null)
			WordInput.TextChanged += OnTextChanged;
    }

	private void OnTextChanged(string newText)
    {
        WordInput.Modulate = new Color(1, 1, 1);
    }

	public void SetupTurn(bool isBlueTeam){
		this.Visible = true;

		if(WordInput != null){
			WordInput.Text = "";
			WordInput.Modulate = new Color(1, 1, 1);
			WordInput.PlaceholderText = "Słowo:";
		}
		if(NumberInput != null)
			NumberInput.Value = 1;
		
		if(SendButton != null){
			SendButton.Modulate = isBlueTeam ? _blueTeamColor : _redTeamColor;
		}
	}

	private void OnSendPressed(){
		if(WordInput == null) return;
		
		string text = WordInput.Text.Trim();
		int number = (int)NumberInput.Value;

		if (text.Contains(" "))
        {
            ShowError("Tylko jedno słowo!");
            return;
        }

		if(!string.IsNullOrEmpty(text)){
			EmitSignal(SignalName.HintGiven, text, number);
			
			this.Visible = false;
		}
	}

	private void ShowError(string message)
    {
        GD.Print($"Błąd: {message}");
        WordInput.Modulate = new Color(1, 0.3f, 0.3f); 
    }
}
