using Godot;
//Nazwa wszedzie powinna byc LoadingScreen.cs
//Jest jakis problem z nazwa skryptu a godotem
public partial class LoadingScreen : Control
{
	public void ShowLoading()
	{
		Visible = true;
		MouseFilter = MouseFilterEnum.Stop;
	}

	public void HideLoading()
	{
		Visible = false;
		MouseFilter = MouseFilterEnum.Ignore;
	}
}
