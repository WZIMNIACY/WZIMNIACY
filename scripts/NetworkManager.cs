using Godot;
using System;

public partial class NetworkManager : Node
{
	// Singleton - pozwala odwoływać się przez NetworkManager.Instance
	public static NetworkManager Instance { get; private set; }

	// Zmienna na peer
	private MultiplayerPeer _multiplayerPeer;

	public override void _Ready()
	{
		Instance = this;
	}

	/// <summary>
	/// Uruchamia serwer gry (Host)
	/// </summary>
	public void StartHost()
	{
		GD.Print("🔌 NetworkManager: Initializing Host...");

		// Próba utworzenia instancji klasy z pluginu
		GodotObject peerInstance = null;
		
		try 
		{
			// Używamy ClassDB, aby ominąć błędy kompilacji C# gdy brakuje wrapperów
			var instance = ClassDB.Instantiate("EOSGMultiplayerPeer");
			peerInstance = instance.As<GodotObject>();
		}
		catch (Exception e)
		{
			GD.PrintErr($"❌ Exception creating peer: {e.Message}");
		}
		
		if (peerInstance == null)
		{
			GD.PrintErr("❌ CRITICAL: Could not instantiate EOSGMultiplayerPeer via ClassDB. Is the plugin enabled?");
			return;
		}

		// Rzutowanie na bazową klasę MultiplayerPeer
		_multiplayerPeer = peerInstance as MultiplayerPeer;

		if (_multiplayerPeer == null)
		{
			 GD.PrintErr("❌ CRITICAL: Instantiated object is not a MultiplayerPeer!");
			 return;
		}

		// Wywołanie metody create_server dynamicznie
		var errorVariant = _multiplayerPeer.Call("create_server", "GameSocket");
		Error error = errorVariant.As<Error>();

		if (error != Error.Ok)
		{
			GD.PrintErr($"❌ Failed to create EOS Server: {error}");
			return;
		}

		// Przypisanie do Godota
		Multiplayer.MultiplayerPeer = _multiplayerPeer;
		GD.Print("📡 NetworkManager: P2P Host Started.");
	}

	/// <summary>
	/// Dołącza do gry jako klient
	/// </summary>
	public void StartClient(string hostProductUserId)
	{
		GD.Print($"🔌 NetworkManager: Connecting to host {hostProductUserId}...");

		if (string.IsNullOrEmpty(hostProductUserId))
		{
			GD.PrintErr("❌ NetworkManager: Host ID is empty!");
			return;
		}

		GodotObject peerInstance = null;
		try 
		{
			var instance = ClassDB.Instantiate("EOSGMultiplayerPeer");
			peerInstance = instance.As<GodotObject>();
		}
		catch (Exception e)
		{
			GD.PrintErr($"❌ Exception creating peer: {e.Message}");
		}
		
		if (peerInstance == null)
		{
			GD.PrintErr("❌ CRITICAL: Could not instantiate EOSGMultiplayerPeer.");
			return;
		}

		_multiplayerPeer = peerInstance as MultiplayerPeer;

		// Wywołanie create_client dynamicznie
		var errorVariant = _multiplayerPeer.Call("create_client", hostProductUserId, "GameSocket");
		Error error = errorVariant.As<Error>();

		if (error != Error.Ok)
		{
			GD.PrintErr($"❌ Failed to create EOS Client: {error}");
			return;
		}

		Multiplayer.MultiplayerPeer = _multiplayerPeer;
		GD.Print("📡 NetworkManager: P2P Client Connecting...");
	}

	/// <summary>
	/// Zamyka połączenie
	/// </summary>
	public void StopNetwork()
	{
		if (_multiplayerPeer != null)
		{
			_multiplayerPeer.Close();
			Multiplayer.MultiplayerPeer = null;
			_multiplayerPeer = null;
			GD.Print("🔌 NetworkManager: Connection closed.");
		}
	}
}
