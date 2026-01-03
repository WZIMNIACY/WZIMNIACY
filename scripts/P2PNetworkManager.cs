using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Godot;
using System;
using System.Diagnostics;

public partial class P2PNetworkManager : Node
{
	[Signal]
	public delegate void ClientReadyEventHandler(string clientPuid);
	[Signal]
	public delegate void AllClientsReadyEventHandler();
	[Signal]
	public delegate void HandshakeCompleteEventHandler();

	// =====================================================
	// RPC TYPES
	// =====================================================
	private enum RpcType
	{
		Hello,
		Welcome,
		Ack
	}

	// =====================================================
	// RPC ENVELOPE + PAYLOADS
	// =====================================================
	private struct RpcEnvelope
	{
		public int Version;
		public string SessionId;
		public RpcType Type;
		public long MessageId;
		public string From;
		public string To;
		public string PayloadJson;
	}

	private struct HelloPayload
	{
		public string Nickname;
		public int ProtocolVersion;
	}

	private struct WelcomePayload
	{
		public bool Accepted;
		public int Nonce;
	}

	private struct AckPayload
	{
		public int Nonce;
		public bool Ready;
	}

	// =====================================================
	// P2P NETWORK MANAGER (STATE & CONFIG)
	// =====================================================
	private const string P2PSocketName = "WZIMNIACY_P2P";  
	private const byte ControlChannel = 0;
	private const int ProtocolVersion = 1;
	private const uint MaxPacketSize = 4096;

	private EOSManager eosManager;
	private P2PInterface p2pInterface;
	private SocketId eosSocketId;

	private bool isHost;
	private string sessionId = "";
	private string localPuid = "";
	private string hostPuid = "";

	private long nextMessageId = 1;

	private int clientExpectedNonce;
	private bool clientHandshakeComplete;
	
	private readonly System.Collections.Generic.HashSet<string> expectedClients = new();
	private readonly System.Collections.Generic.HashSet<string> readyClients = new();

	private enum HandshakeState
	{
		NotStarted,
		Handshaking,
		Ready
	}

	private HandshakeState handshakeState = HandshakeState.NotStarted;

	public void StartAsHost(string sessionId, string localPuid, System.Collections.Generic.IEnumerable<string> expectedClients)
	{
		GD.Print("[P2P] StartAsHost called");

		isHost = true;
		this.sessionId = sessionId;
		this.localPuid = localPuid;
		hostPuid = localPuid;

		this.expectedClients.Clear();
		readyClients.Clear();

		foreach (var client in expectedClients)
			this.expectedClients.Add(client);

		GD.Print($"[P2P] Host start. sessionId={this.sessionId}, expectedClients={this.expectedClients.Count}");

		handshakeState = HandshakeState.Handshaking;

		GD.Print($"[P2P] Host start debug localPuid={localPuid} socket={eosSocketId.SocketName}");

	}

	public void StartAsClient(string sessionId, string localPuid, string hostPuid)
	{
		GD.Print("[P2P] StartAsClient called");

		isHost = false;
		this.sessionId = sessionId;
		this.localPuid = localPuid;
		this.hostPuid = hostPuid;

		clientExpectedNonce = 0;
		clientHandshakeComplete = false;

		GD.Print($"[P2P] Client start. sessionId={this.sessionId}, host={this.hostPuid}");

		handshakeState = HandshakeState.Handshaking;
		SendHello();
		GD.Print($"[P2P] Client start debug localPuid={localPuid} host={hostPuid} socket={eosSocketId.SocketName}");


	}

	// =====================================================
	// SERIALIZATION (ENCODE / DECODE)
	// =====================================================
	private static readonly JsonSerializerOptions jsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false
	};

	private long NextMessageId()
	{
		var id = nextMessageId;
		nextMessageId++;
		return id;
	}

	private string ToPayloadJson<TPayload>(TPayload payload)
	{
		return JsonSerializer.Serialize(payload, jsonOptions);
	}

	private TPayload FromPayloadJson<TPayload>(string payloadJson)
	{
		var payload = JsonSerializer.Deserialize<TPayload>(payloadJson, jsonOptions);
		if (payload == null)
			throw new InvalidOperationException($"Failed to deserialize payload: {typeof(TPayload).Name}");

		return payload;
	}

	private byte[] EncodeMessage(RpcType type, string to, string payloadJson)
	{
		var envelope = new RpcEnvelope
		{
			Version = 1,
			SessionId = sessionId,
			Type = type,
			MessageId = NextMessageId(),
			From = localPuid,
			To = to,
			PayloadJson = payloadJson
		};

		var json = JsonSerializer.Serialize(envelope, jsonOptions);
		return Encoding.UTF8.GetBytes(json);
	}

	private RpcEnvelope DecodeMessage(byte[] data)
	{
		var json = Encoding.UTF8.GetString(data);
		var envelope = JsonSerializer.Deserialize<RpcEnvelope>(json, jsonOptions);
		if (envelope.SessionId == null)
			throw new InvalidOperationException("Failed to deserialize RPC envelope.");

		return envelope;
	}


	private byte[] EncodeMessage<TPayload>(RpcType type, string to, TPayload payload)
	{
		if (string.IsNullOrEmpty(sessionId))
			throw new InvalidOperationException("sessionId is not set. Call StartAsHost/StartAsClient first.");

		var payloadJson = ToPayloadJson(payload);
		return EncodeMessage(type, to, payloadJson);
	}

	private TPayload DecodePayload<TPayload>(RpcEnvelope envelope)
	{
		return FromPayloadJson<TPayload>(envelope.PayloadJson);
	}


	// =====================================================
	// GODOT LIFECYCLE (READY / PROCESS)
	// =====================================================
	private int ProcessTick;

	public override void _Process(double delta)
	{

		ProcessTick++;
		if(ProcessTick % 60 == 0)
		{
			GD.Print("[P2P] _Process tick (PollIncoming runnning)");
		}
		PollIncoming();
	}
	public override void _Ready()
	{
		GD.Print("[P2P] P2PNetworkManager _Ready fired");

		eosManager = GetNode<EOSManager>("/root/EOSManager");

		p2pInterface = eosManager.PlatformInterface.GetP2PInterface();

		eosSocketId = new SocketId { SocketName = P2PSocketName };


		GD.Print("[P2P] EOS P2P initialized");


	}

	public override void _ExitTree()
	{
		
	}

	// =====================================================
	// 	RECEIVE LOOP + DISPATCH
	// =====================================================
	private void PollIncoming()
	{
		while (TryReceivePacket(out var fromPuid, out var data))
		{
			GD.Print($"[P2P] PollIncoming got raw packet from={fromPuid} bytes={data.Length}");

			RpcEnvelope envelope;

			try
			{
				envelope = DecodeMessage(data);
			}
			catch (Exception e)
			{
				GD.PrintErr($"[P2P] Decode failed: {e.Message}");
				continue;
			}

			if (envelope.SessionId != sessionId)
				continue;

			Dispatch(fromPuid, envelope);
		}
	}


	private void Dispatch(string fromPuid, RpcEnvelope envelope)
	{
		GD.Print($"[P2P] Dispatch type={envelope.Type} from={fromPuid} msgId={envelope.MessageId}");

		switch (envelope.Type)
		{
			case RpcType.Hello:
				if (isHost)
					HandleHello(fromPuid, envelope);
				break;

			case RpcType.Welcome:
				if (!isHost)
					HandleWelcome(fromPuid, envelope);
				break;

			case RpcType.Ack:
				if (isHost)
					HandleAck(fromPuid, envelope);
				break;
		}
	}

	// =====================================================
	// HANDSHAKE (SEND + HANDLERS)
	// =====================================================
	private void SendHello()
	{
		var payload = new HelloPayload
		{
			Nickname = localPuid,
			ProtocolVersion = ProtocolVersion
		};

		var data = EncodeMessage(RpcType.Hello, hostPuid, payload);
		SendPacket(hostPuid, ControlChannel, data);

		GD.Print($"[P2P] Sent HELLO to {hostPuid}");
	}

	private void SendWelcome(string clientPuid, int nonce)
	{
		var payload = new WelcomePayload
		{
			Accepted = true,
			Nonce = nonce
		};

		var data = EncodeMessage(RpcType.Welcome, clientPuid, payload);
		SendPacket(clientPuid, ControlChannel, data);

		GD.Print($"[P2P] Sent WELCOME to {clientPuid} nonce={nonce}");
	}

	private void SendAck(int nonce)
	{
		var payload = new AckPayload
		{
			Nonce = nonce,
			Ready = true
		};

		var data = EncodeMessage(RpcType.Ack, hostPuid, payload);
		SendPacket(hostPuid, ControlChannel, data);

		GD.Print($"[P2P] Sent ACK to {hostPuid} nonce={nonce}");
	}
	private void HandleHello(string fromPuid, RpcEnvelope envelope)
	{
		// Host dostaje HELLO od klienta
		HelloPayload payload;

		try
		{
			payload = DecodePayload<HelloPayload>(envelope);
		}
		catch (Exception e)
		{
			GD.PrintErr($"[P2P] Invalid HELLO payload from {fromPuid}: {e.Message}");
			return;
		}

		if (!expectedClients.Contains(fromPuid))
		{
			GD.PrintErr($"[P2P] HELLO from unexpected client: {fromPuid}");
			return;
		}

		if (payload.ProtocolVersion != ProtocolVersion)
		{
			GD.PrintErr($"[P2P] Protocol mismatch from {fromPuid}: {payload.ProtocolVersion} != {ProtocolVersion}");
			return;
		}

		var nonce = Random.Shared.Next(100000, 999999);

		// Możemy trzymać nonce per klient później; na dziś wystarczy, że klient odsyła to samo,
		// a my po ACK po prostu uznajemy go za ready. (Minimalny handshake)
		SendWelcome(fromPuid, nonce);
	}

	private void HandleWelcome(string fromPuid, RpcEnvelope envelope)
	{
		// Klient dostaje WELCOME od hosta
		WelcomePayload payload;

		try
		{
			payload = DecodePayload<WelcomePayload>(envelope);
		}
		catch (Exception e)
		{
			GD.PrintErr($"[P2P] Invalid WELCOME payload: {e.Message}");
			return;
		}

		if (fromPuid != hostPuid)
		{
			GD.PrintErr($"[P2P] WELCOME from non-host: {fromPuid}");
			return;
		}

		if (!payload.Accepted)
		{
			GD.PrintErr("[P2P] Host rejected handshake.");
			return;
		}

		clientExpectedNonce = payload.Nonce;
		SendAck(clientExpectedNonce);

		clientHandshakeComplete = true;
		handshakeState = HandshakeState.Ready;
		EmitSignal(SignalName.HandshakeComplete);

		GD.Print("[P2P] Client handshake complete.");
	}

	private void HandleAck(string fromPuid, RpcEnvelope envelope)
	{
		// Host dostaje ACK od klienta
		AckPayload payload;

		try
		{
			payload = DecodePayload<AckPayload>(envelope);
		}
		catch (Exception e)
		{
			GD.PrintErr($"[P2P] Invalid ACK payload from {fromPuid}: {e.Message}");
			return;
		}

		if (!expectedClients.Contains(fromPuid))
		{
			GD.PrintErr($"[P2P] ACK from unexpected client: {fromPuid}");
			return;
		}

		if (!payload.Ready)
			return;

		// Minimalnie: uznajemy klienta za gotowego
		if (readyClients.Add(fromPuid))
		{
			EmitSignal(SignalName.ClientReady, fromPuid);
			GD.Print($"[P2P] Client ready: {fromPuid} ({readyClients.Count}/{expectedClients.Count})");
		}

		if (readyClients.Count == expectedClients.Count)
		{
			handshakeState = HandshakeState.Ready;
			EmitSignal(SignalName.AllClientsReady);
			EmitSignal(SignalName.HandshakeComplete);

			GD.Print("[P2P] All clients ready. Host handshake complete.");
		}
	}

	// =====================================================
	// TRANSPORT (EOS P2P SEND / RECEIVE)
	// =====================================================
	private void SendPacket(string toPuid, byte channel, byte[] data)
	{
		GD.Print($"[P2P] SendPacket to={toPuid} socket={eosSocketId.SocketName} channel={channel} bytes={data.Length}");

		var localUserId = ProductUserId.FromString(localPuid);

		var options = new SendPacketOptions
		{
			LocalUserId = localUserId,
			RemoteUserId = ProductUserId.FromString(toPuid),
			SocketId = eosSocketId,
			Channel = channel,
			Data = data,
			Reliability = PacketReliability.ReliableOrdered
		};

		var result = p2pInterface.SendPacket(ref options);

		if (result != Result.Success)
		{
			GD.PrintErr($"[P2P] SendPacket failed: {result}");
		}
	}

	private bool TryReceivePacket(out string fromPuid, out byte[] data)
	{
		fromPuid = "";
		data = Array.Empty<byte>();

		if (string.IsNullOrEmpty(localPuid))
			return false;

		var localUserId = ProductUserId.FromString(localPuid);
		if (localUserId == null || !localUserId.IsValid())
			return false;

		var buffer = new byte[MaxPacketSize];

		var options = new ReceivePacketOptions
		{
			LocalUserId = localUserId,
			MaxDataSizeBytes = MaxPacketSize
			// Jeśli masz w SDK pole RequestedChannel i chcesz filtrować:
			// RequestedChannel = ControlChannel
		};

		var remoteUserId = new ProductUserId();
		var socketId = new SocketId();
		byte channel;
		uint bytesWritten;

		var result = p2pInterface.ReceivePacket(
			ref options,
			ref remoteUserId,
			ref socketId,
			out channel,
			new ArraySegment<byte>(buffer),
			out bytesWritten
		);

		if (result == Result.NotFound)
			return false;

		GD.PrintErr($"[P2P] ReceivePacket returned {result}");


		if (result != Result.Success)
		{
			GD.PrintErr($"[P2P] ReceivePacket failed: {result}");
			return false;
		}

		if (!remoteUserId.IsValid())
		{
			GD.PrintErr("[P2P] ReceivePacket returned invalid RemoteUserId");
			return false;
		}

		if (bytesWritten == 0 || bytesWritten > (uint)buffer.Length)
		{
			GD.PrintErr($"[P2P] ReceivePacket invalid bytesWritten={bytesWritten}");
			return false;
		}

		fromPuid = remoteUserId.ToString();

		data = new byte[bytesWritten];
		Array.Copy(buffer, data, (int)bytesWritten);

		// Debug (odkomentuj na chwilę, jak trzeba):
		// GD.Print($"[P2P] ReceivePacket SUCCESS from={fromPuid} bytes={bytesWritten} socket={socketId.SocketName} channel={channel}");

		return true;
	}

}
