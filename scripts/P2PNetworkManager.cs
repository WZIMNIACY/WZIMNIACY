using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;

/// <summary>
/// Node do obsługi podstawowego P2P w EOS.
///
/// Etap 1 (to co teraz robimy):
/// - Ustalamy SocketId (SocketName = sessionId) – to jest „klucz” sesji, po którym rozpoznajemy właściwe połączenie.
/// - Wymieniamy handshake: CLIENT_HELLO -> HOST_WELCOME.
/// - Trzymamy listę peerów, z którymi mamy aktywny handshake.
///
/// W praktyce EOS P2P nie ma „open channel” jak w TCP. Kanał jest logiczny (byte Channel) i działa dopiero
/// gdy zaczniesz wysyłać/odbierać pakiety na danym SocketId i Channel.
/// </summary>
public partial class P2PNetworkManager : Node
{
    // Na razie: jeden kanał na wszystko (handshake + późniejsza gra).
    // Zgodnie z Twoją uwagą: Channel=channel, więc łatwo będzie zmienić później.
    private const byte DefaultChannel = 0;

    // Handshake jest mały, ale trzymajmy sensowny limit bufora.
    private const int MaxPacketSizeBytes = 4096;

    // Co ile klient ponawia HELLO, dopóki nie dostanie WELCOME.
    private const double ClientHelloRetrySeconds = 1.0;

    // EOS / P2P
    private EOSManager eosManager;
    private P2PInterface p2pInterface;

    // Kontekst sesji
    private bool isHost;
    private string sessionId;
    private SocketId socketId;

    private ProductUserId localPuid;
    private ProductUserId hostPuid;

    // Połączenia
    private readonly HashSet<string> connectedPeerPuids = new HashSet<string>(StringComparer.Ordinal);

    // Klient: retry timer
    private double helloRetryAccumulator;
    private bool clientHandshakeComplete;

    // Host: ograniczenie spamowania WELCOME przy retry HELLO (np. gdy WELCOME jeszcze nie dotarło)
    // Key = peerPuidString, Value = ostatni czas wysłania WELCOME (ms)
    private readonly Dictionary<string, long> lastWelcomeSentAtMs = new Dictionary<string, long>(StringComparer.Ordinal);

    // Prosty format wiadomości
    private enum MsgType : byte
    {
        ClientHello = 1,
        HostWelcome = 2
    }

    [Signal]
    public delegate void PeerConnectedEventHandler(string peerPuid);

    [Signal]
    public delegate void PeerDisconnectedEventHandler(string peerPuid);

    [Signal]
    public delegate void HostHandshakeReadyEventHandler();

    [Signal]
    public delegate void ClientHandshakeReadyEventHandler();

    public override void _Ready()
    {
        base._Ready();

        // EOSManager jest autoloadem w /root/EOSManager w Twoim projekcie.
        eosManager = GetNodeOrNull<EOSManager>("/root/EOSManager");
        if (eosManager == null)
        {
            GD.PrintErr("[P2PNetworkManager] EOSManager not found at /root/EOSManager");
            return;
        }

        if (eosManager.PlatformInterface == null)
        {
            GD.PrintErr("[P2PNetworkManager] eosManager.PlatformInterface is null (EOS not initialized?)");
            return;
        }

        p2pInterface = eosManager.PlatformInterface.GetP2PInterface();
        if (p2pInterface == null)
        {
            GD.PrintErr("[P2PNetworkManager] Failed to get P2PInterface from EOS PlatformInterface");
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (p2pInterface == null || localPuid == null || !localPuid.IsValid())
        {
            return;
        }

        // 1) Odbieranie pakietów (pętla while)
        while (TryReceivePacket(out var packet))
        {
            HandlePacket(packet.peerPuid, packet.socketName, packet.channel, packet.payload);
        }

        // 2) Klient: ponawianie HELLO dopóki nie ma WELCOME
        if (!isHost && !clientHandshakeComplete && hostPuid != null && hostPuid.IsValid())
        {
            helloRetryAccumulator += delta;
            if (helloRetryAccumulator >= ClientHelloRetrySeconds)
            {
                helloRetryAccumulator = 0;
                SendClientHello();
            }
        }
    }

    /// <summary>
    /// Wywoływane przez MainGame gdy lokalny gracz jest hostem.
    /// sessionId: krótki identyfikator sesji (u Was 8 znaków).
    /// localPuidString: eosManager.localProductUserIdString.
    /// clientPuids: lista PU... w wersji docelowej tu przekażesz listę graczy z lobby.
    /// </summary>
    public void StartAsHost(string sessionId, string localPuidString, string[] clientPuids)
    {
        isHost = true;
        clientHandshakeComplete = false;
        helloRetryAccumulator = 0;

        if (!PrepareCommon(sessionId, localPuidString))
        {
            return;
        }

        GD.Print($"[P2PNetworkManager] StartAsHost sessionId={this.sessionId} local={localPuid}");

        // Host na tym etapie nie musi nic wysyłać od razu.
        // Po prostu czeka na HELLO.

        // (Opcjonalnie) jeśli chcesz od razu zainicjować połączenia do znanych klientów,
        // można wysłać im WELCOME/probe – ale w EOS to zwykle klient inicjuje.

        EmitSignal(SignalName.HostHandshakeReady);
    }

    /// <summary>
    /// Wywoływane przez MainGame gdy lokalny gracz jest klientem.
    /// hostPuidString: pobieracie z EOSManager.GetLobbyOwnerPuidString().
    /// </summary>
    public void StartAsClient(string sessionId, string localPuidString, string hostPuidString)
    {
        isHost = false;
        clientHandshakeComplete = false;
        helloRetryAccumulator = 0;

        if (!PrepareCommon(sessionId, localPuidString))
        {
            return;
        }

        hostPuid = ProductUserId.FromString(hostPuidString);
        if (hostPuid == null || !hostPuid.IsValid())
        {
            GD.PrintErr($"[P2PNetworkManager] StartAsClient invalid hostPuidString={hostPuidString}");
            return;
        }

        GD.Print($"[P2PNetworkManager] StartAsClient sessionId={this.sessionId} local={localPuid} host={hostPuid}");

        // Wyślij pierwsze HELLO od razu (a potem retry w _Process).
        SendClientHello();
    }

    private bool PrepareCommon(string sessionId, string localPuidString)
    {
        if (p2pInterface == null)
        {
            GD.PrintErr("[P2PNetworkManager] p2pInterface is null");
            return false;
        }

        if (string.IsNullOrEmpty(sessionId))
        {
            GD.PrintErr("[P2PNetworkManager] sessionId is null/empty");
            return false;
        }

        // SocketName ma limit 32 ASCII znaków (w SocketId.cs widać MaxSocketNameLength=32).
        // U Was sessionId ma 8 znaków, więc jest ok.
        this.sessionId = sessionId;

        localPuid = ProductUserId.FromString(localPuidString);
        if (localPuid == null || !localPuid.IsValid())
        {
            GD.PrintErr($"[P2PNetworkManager] invalid localPuidString={localPuidString}");
            return false;
        }

        socketId = SocketId.Empty;
        socketId.SocketName = this.sessionId;

        connectedPeerPuids.Clear();
        lastWelcomeSentAtMs.Clear();

        return true;
    }

    private void SendClientHello()
    {
        if (clientHandshakeComplete)
        {
            return;
        }

        if (hostPuid == null || !hostPuid.IsValid())
        {
            return;
        }

        // Payload: [MsgType][UTF8:localPuidString]
        string helloText = localPuid.ToString();
        byte[] textBytes = Encoding.UTF8.GetBytes(helloText);

        byte[] payload = new byte[1 + textBytes.Length];
        payload[0] = (byte)MsgType.ClientHello;
        Buffer.BlockCopy(textBytes, 0, payload, 1, textBytes.Length);

        Result result = SendPacket(hostPuid, payload, DefaultChannel);
        if (result == Result.Success)
        {
            GD.Print($"[P2PNetworkManager] -> HELLO to host={hostPuid} socket={socketId.SocketName}");
        }
        else
        {
            GD.PrintErr($"[P2PNetworkManager] Send HELLO failed: {result}");
        }
    }

    private void SendHostWelcome(ProductUserId remotePeer)
    {
        // Payload: [MsgType][UTF8:sessionId]
        byte[] textBytes = Encoding.UTF8.GetBytes(sessionId);
        byte[] payload = new byte[1 + textBytes.Length];
        payload[0] = (byte)MsgType.HostWelcome;
        Buffer.BlockCopy(textBytes, 0, payload, 1, textBytes.Length);

        Result result = SendPacket(remotePeer, payload, DefaultChannel);
        if (result == Result.Success)
        {
            GD.Print($"[P2PNetworkManager] -> WELCOME to peer={remotePeer} socket={socketId.SocketName}");
        }
        else
        {
            GD.PrintErr($"[P2PNetworkManager] Send WELCOME failed: {result}");
        }
    }

    private Result SendPacket(ProductUserId remotePeer, byte[] payload, byte channel)
    {
        if (payload == null)
        {
            return Result.InvalidParameters;
        }

        // Uwaga: w Twoich plikach widać struct SendPacketOptions i SocketId.
        // Metoda P2PInterface.SendPacket jest w wrapperze EOS SDK (część plików może być w innym miejscu projektu).
        var options = new SendPacketOptions
        {
            LocalUserId = localPuid,
            RemoteUserId = remotePeer,
            SocketId = socketId,
            Channel = channel,
            Data = new ArraySegment<byte>(payload),
            AllowDelayedDelivery = true,
            Reliability = PacketReliability.ReliableOrdered,
            DisableAutoAcceptConnection = false
        };

        return p2pInterface.SendPacket(ref options);
    }

    private struct ReceivedPacket
    {
        public string peerPuid;
        public string socketName;
        public byte channel;
        public byte[] payload;
    }

    /// <summary>
    /// Pełna pętla odbioru: najpierw pytamy o rozmiar, potem odbieramy.
    /// Zwraca true gdy rzeczywiście odebraliśmy pakiet.
    /// </summary>
    private bool TryReceivePacket(out ReceivedPacket packet)
    {
        packet = default;

        // 1) Sprawdź rozmiar następnego pakietu
        var sizeOptions = new GetNextReceivedPacketSizeOptions
        {
            LocalUserId = localPuid,
            RequestedChannel = null // null = dowolny kanał
        };

        uint nextSize = 0;
        Result sizeResult = p2pInterface.GetNextReceivedPacketSize(ref sizeOptions, out nextSize);

        if (sizeResult == Result.NotFound)
        {
            return false;
        }

        if (sizeResult != Result.Success)
        {
            GD.PrintErr($"[P2PNetworkManager] GetNextReceivedPacketSize failed: {sizeResult}");
            return false;
        }

        if (nextSize == 0)
        {
            return false;
        }

        if (nextSize > MaxPacketSizeBytes)
        {
            GD.PrintErr($"[P2PNetworkManager] Incoming packet too large: {nextSize} bytes");
            // Spróbujemy go mimo wszystko odebrać do limitu, ale to może uciąć dane.
            nextSize = MaxPacketSizeBytes;
        }

        // 2) Odbierz
        var receiveOptions = new ReceivePacketOptions
        {
            LocalUserId = localPuid,
            MaxDataSizeBytes = nextSize,
            RequestedChannel = null
        };

        var outPeerId = (ProductUserId)null;
        var outSocketId = SocketId.Empty;
        byte outChannel;
        var outData = new byte[nextSize];
        uint outBytesWritten;

        Result recvResult = p2pInterface.ReceivePacket(
            ref receiveOptions,
            ref outPeerId,
            ref outSocketId,
            out outChannel,
            new ArraySegment<byte>(outData),
            out outBytesWritten
        );

        if (recvResult == Result.NotFound)
        {
            return false;
        }

        if (recvResult != Result.Success)
        {
            GD.PrintErr($"[P2PNetworkManager] ReceivePacket failed: {recvResult}");
            return false;
        }

        if (outBytesWritten == 0)
        {
            return false;
        }

        // Przytnij do faktycznej długości
        byte[] trimmed = new byte[outBytesWritten];
        Buffer.BlockCopy(outData, 0, trimmed, 0, (int)outBytesWritten);

        packet = new ReceivedPacket
        {
            peerPuid = outPeerId?.ToString() ?? "",
            socketName = outSocketId.SocketName ?? "",
            channel = outChannel,
            payload = trimmed
        };

        return true;
    }

    private void HandlePacket(string peerPuidString, string socketName, byte channel, byte[] payload)
    {
        if (string.IsNullOrEmpty(peerPuidString) || payload == null || payload.Length < 1)
        {
            return;
        }

        // Filtr bezpieczeństwa: reagujemy tylko na nasz SocketId
        if (!string.Equals(socketName, sessionId, StringComparison.Ordinal))
        {
            GD.Print($"[P2PNetworkManager] Ignoring packet on socket='{socketName}', expected='{sessionId}'");
            return;
        }

        MsgType type = (MsgType)payload[0];

        switch (type)
        {
            case MsgType.ClientHello:
                HandleClientHello(peerPuidString, channel, payload);
                break;

            case MsgType.HostWelcome:
                HandleHostWelcome(peerPuidString, channel, payload);
                break;

            default:
                GD.Print($"[P2PNetworkManager] Unknown msg type={payload[0]} from {peerPuidString}");
                break;
        }
    }

    private void HandleClientHello(string peerPuidString, byte channel, byte[] payload)
    {
        if (!isHost)
        {
            // Klient ignoruje HELLO od innych klientów
            return;
        }

        // payload: [type][utf8:clientPuid]
        string declaredClientPuid = DecodeUtf8(payload, 1);
        if (!string.IsNullOrEmpty(declaredClientPuid) && !string.Equals(declaredClientPuid, peerPuidString, StringComparison.Ordinal))
        {
            GD.Print($"[P2PNetworkManager] Warning: HELLO declared='{declaredClientPuid}', real='{peerPuidString}'");
        }

        if (!connectedPeerPuids.Contains(peerPuidString))
        {
            connectedPeerPuids.Add(peerPuidString);
            GD.Print($"[P2PNetworkManager] Host connected peer={peerPuidString} (channel={channel})");
            EmitSignal(SignalName.PeerConnected, peerPuidString);
        }

        // Odpowiedz WELCOME
        // Klient może retry'ować HELLO, zanim dostanie WELCOME. Żeby nie spamować, resend maks. co ~750ms.
        long nowMs = (long)Time.GetTicksMsec();
        bool shouldSendWelcome = true;
        bool isResend = false;

        if (lastWelcomeSentAtMs.TryGetValue(peerPuidString, out long lastMs))
        {
            isResend = true;
            if (nowMs - lastMs < 750)
            {
                shouldSendWelcome = false;
            }
        }

        if (shouldSendWelcome)
        {
            lastWelcomeSentAtMs[peerPuidString] = nowMs;

            ProductUserId remotePeer = ProductUserId.FromString(peerPuidString);
            if (remotePeer != null && remotePeer.IsValid())
            {
                if (!isResend)
                {
                    GD.Print($"[P2PNetworkManager] HOST <- HELLO from {peerPuidString} (sending WELCOME)");
                }
                else
                {
                    GD.Print($"[P2PNetworkManager] HOST <- HELLO again from {peerPuidString} (re-sending WELCOME)");
                }

                SendHostWelcome(remotePeer);
            }
        }
    }

    private void HandleHostWelcome(string peerPuidString, byte channel, byte[] payload)
    {
        if (isHost)
        {
            // Host ignoruje WELCOME
            return;
        }

        // Jeśli już zakończyliśmy handshake, ignorujemy duplikaty WELCOME
        if (clientHandshakeComplete)
        {
            return;
        }

        // payload: [type][utf8:sessionId]
        string welcomeSessionId = DecodeUtf8(payload, 1);

        // Minimalna walidacja
        if (!string.Equals(welcomeSessionId, sessionId, StringComparison.Ordinal))
        {
            GD.PrintErr($"[P2PNetworkManager] Received WELCOME for different sessionId='{welcomeSessionId}', expected='{sessionId}'");
            return;
        }

        // Akceptujemy handshake
        clientHandshakeComplete = true;

        if (!connectedPeerPuids.Contains(peerPuidString))
        {
            connectedPeerPuids.Add(peerPuidString);
        }

        GD.Print($"[P2PNetworkManager] Client handshake complete with host={peerPuidString} (channel={channel})");
        EmitSignal(SignalName.ClientHandshakeReady);
    }

    private static string DecodeUtf8(byte[] data, int offset)
    {
        if (data == null || data.Length <= offset)
        {
            return "";
        }

        try
        {
            return Encoding.UTF8.GetString(data, offset, data.Length - offset);
        }
        catch
        {
            return "";
        }
    }

    public bool IsHandshakeReady()
    {
        if (isHost)
        {
            // Host jest „ready” od razu – będzie zbierał klientów. Możesz tu dodać warunek: min 1 klient.
            return true;
        }

        return clientHandshakeComplete;
    }

    public string[] GetConnectedPeers()
    {
        var arr = new string[connectedPeerPuids.Count];
        connectedPeerPuids.CopyTo(arr);
        return arr;
    }
}
