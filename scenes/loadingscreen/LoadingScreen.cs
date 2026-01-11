using Godot;

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
