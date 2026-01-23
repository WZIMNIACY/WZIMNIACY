// P2PNetworkManager.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;

/// <summary>
/// Manages Peer-to-Peer networking using Epic Online Services (EOS).
/// Handles handshake, packet routing, and JSON-based RPC messaging.
/// </summary>
public partial class P2PNetworkManager : Node
{
    // Zgodnie z Twoim planem: handshake + gra na tym samym kanale 0
    private const byte Channel = 0;

    // Handshake messages (PLAIN TEXT, NIE JSON)
    private const string MsgClientHello = "CLIENT_HELLO";
    private const string MsgHostWelcome = "HOST_WELCOME";
    private const string MsgHostPing = "HOST_PING";

    // Retry (client)
    private const double ClientHelloRetrySeconds = 0.7;
    private double helloRetryAccumulator = 0.0;

    private EOSManager eosManager;
    private P2PInterface p2pInterface;

    private bool started = false;
    private bool isHost = false;

    private string sessionId;
    private SocketId socketId;

    private ProductUserId localPuid;
    private ProductUserId hostPuid;

    // Host: lista klientów (PUID)
    private readonly Dictionary<string, ProductUserId> hostClients = new();
    // Handshake state
    private bool clientHandshakeComplete = false;
    private readonly HashSet<string> hostWelcomedClients = new();

    // === HOST START GAME (timeout + game_start) ===
    private const double HostStartTimeoutSeconds = 12.0;
    private double hostStartElapsedSeconds = 0.0;
    private bool hostStartCountdownActive = false;
    private bool hostGameStartSent = false;
    // =============================================


    // === JSON RPC (PUBLICZNE MODELE) ===
    /// <summary>
    /// Represents a generic network message for JSON RPC.
    /// </summary>
    public sealed class NetMessage
    {
        public string kind { get; set; }    // "rpc"
        public string type { get; set; }    // np. "card_selected"
        public JsonElement payload { get; set; } // dowolny obiekt payload
    }

    // === GAME START RPC MODELS ===
    /// <summary>
    /// Represents a player in the game session.
    /// </summary>
    public sealed class GamePlayer
    {
        public int index { get; set; }          // 0 = host, 1..N = klienci
        public string puid { get; set; }        // ProductUserId jako string
        public string name { get; set; }        // opcjonalnie (może być null)
        public MainGame.Team team { get; set; }        // enum Team
        public string profileIconPath { get; set; }
    }

    /// <summary>
    /// Payload data for starting a game session.
    /// </summary>
    public sealed class GameStartPayload
    {
        public string sessionId { get; set; }
        public GamePlayer[] players { get; set; }
        public string startingTeam { get; set; } // "Blue" / "Red"
        public ulong seed { get; set; }          // wspólny seed rozgrywki

    }

    // Host może (opcjonalnie) zbudować payload z dodatkowymi danymi (name/team)
    // żeby P2P nie znał EOSManager.
    /// <summary>
    /// Delegate/Func to build the game start payload on the host.
    /// Allows customization of the initial game state.
    /// </summary>
    public Func<GameStartPayload> hostBuildGameStartPayload;
    // =============================


    // === HANDLERY PAKIETÓW (router) ===
    // Handler zwraca true jeśli "zjada" pakiet (czyli był obsłużony)
    /// <summary>
    /// Delegate for handling incoming network packets.
    /// </summary>
    /// <param name="packet">The received network message.</param>
    /// <param name="fromPeer">The ID of the peer who sent the message.</param>
    /// <returns>True if the packet was handled; otherwise, false.</returns>
    public delegate bool PacketHandler(NetMessage packet, ProductUserId fromPeer);

    /// <summary>
    /// Event triggered when a packet is received.
    /// </summary>
    public event PacketHandler PacketHandlers;

    // --- Przydatne property (do użycia w MainGame / innych plikach) ---
    /// <summary>
    /// Gets a value indicating whether this instance is acting as the host.
    /// </summary>
    public bool IsHost => isHost;
    /// <summary>
    /// Gets a value indicating whether the network manager has started.
    /// </summary>
    public bool Started => started;
    /// <summary>
    /// Gets a value indicating whether the client handshake with the host is complete.
    /// </summary>
    public bool ClientHandshakeComplete => clientHandshakeComplete; // tylko sensowne po stronie klienta
    /// <summary>
    /// Gets the current session ID.
    /// </summary>
    public string SessionId => sessionId;
    /// <summary>
    /// Gets the local Product User ID.
    /// </summary>
    public ProductUserId LocalPuid => localPuid;
    /// <summary>
    /// Gets the host's Product User ID.
    /// </summary>
    public ProductUserId HostPuid => hostPuid;

    // === HANDSHAKE EVENT (DODANE) ===
    /// <summary>
    /// Event fired when the handshake process is successfully completed.
    /// </summary>
    public event Action HandshakeCompleted;

    /// <summary>
    /// Invokes the HandshakeCompleted event.
    /// </summary>
    private void FireHandshakeCompletedOnce()
    {
        HandshakeCompleted?.Invoke();
    }
    // ===============================

    public override void _Ready()
    {
        eosManager = GetNodeOrNull<EOSManager>("/root/EOSManager");
        if (eosManager == null)
        {
            GD.PrintErr("[P2PNetworkManager] ❌ Nie znaleziono /root/EOSManager");
            return;
        }

        p2pInterface = eosManager.PlatformInterface.GetP2PInterface();

        if (p2pInterface == null)
        {
            GD.PrintErr("[P2PNetworkManager] ❌ P2PInterface = null");
            return;
        }
    }

    public override void _Process(double delta)
    {
        if (!started || p2pInterface == null || localPuid == null || !localPuid.IsValid())
            return;

        // 1) Odbieranie pakietów
        while (TryReceivePacket(out var packet))
        {
            HandlePacket(packet.peerPuid, packet.socketName, packet.channel, packet.payload);
        }

        // 2) Klient: wysyłaj HELLO aż dostaniesz WELCOME
        if (!isHost && !clientHandshakeComplete && hostPuid != null && hostPuid.IsValid())
        {
            helloRetryAccumulator += delta;
            if (helloRetryAccumulator >= ClientHelloRetrySeconds)
            {
                helloRetryAccumulator = 0.0;
                SendClientHello();
            }
        }
        // 3) Host: czekaj na wszystkich welcomed albo timeout -> start gry
        if (isHost && hostStartCountdownActive && !hostGameStartSent)
        {
            hostStartElapsedSeconds += delta;

            bool everyoneWelcomed = hostWelcomedClients.Count == hostClients.Count;
            bool timeoutReached = hostStartElapsedSeconds >= HostStartTimeoutSeconds;

            if (everyoneWelcomed || timeoutReached)
            {
                GD.Print($"[P2PNetworkManager] HOST start condition met. welcomed={hostWelcomedClients.Count}/{hostClients.Count} timeout={hostStartElapsedSeconds:0.00}s");
                SendGameStartToWelcomedClients();
            }
        }

    }

    // HOST start
    /// <summary>
    /// Starts the P2P session as a host.
    /// </summary>
    /// <param name="sessionId">The unique session identifier.</param>
    /// <param name="localPuidString">The local user's Product User ID string.</param>
    /// <param name="clientPuids">Array of client Product User ID strings expected to join.</param>
    public void StartAsHost(string sessionId, string localPuidString, string[] clientPuids)
    {
        if (!EnsureReady(localPuidString)) return;

        started = true;
        isHost = true;
        this.sessionId = sessionId;

        socketId = new SocketId();
        socketId.SocketName = sessionId;

        hostClients.Clear();
        hostWelcomedClients.Clear();

        hostStartElapsedSeconds = 0.0;
        hostStartCountdownActive = true;
        hostGameStartSent = false;


        if (clientPuids != null)
        {
            foreach (var puidStr in clientPuids)
            {
                if (string.IsNullOrEmpty(puidStr)) continue;
                if (puidStr == localPuidString) continue;

                var puid = ProductUserId.FromString(puidStr);
                if (puid != null && puid.IsValid())
                    hostClients[puidStr] = puid;
            }
        }

        GD.Print($"[P2PNetworkManager] StartAsHost sessionId={sessionId} local={localPuidString} clients={hostClients.Count}");

        // KLUCZ: host wysyła pierwszy pakiet do każdego klienta na tym SocketId
        // żeby EOS "poznał" socket i nie wywalał "unknown socket".
        foreach (var kv in hostClients)
        {
            SendRaw(kv.Value, MsgHostPing);
        }
    }

    // CLIENT start
    /// <summary>
    /// Starts the P2P session as a client.
    /// </summary>
    /// <param name="sessionId">The unique session identifier.</param>
    /// <param name="localPuidString">The local user's Product User ID string.</param>
    /// <param name="hostPuidString">The host's Product User ID string.</param>
    public void StartAsClient(string sessionId, string localPuidString, string hostPuidString)
    {
        if (!EnsureReady(localPuidString)) return;

        started = true;
        isHost = false;
        this.sessionId = sessionId;

        socketId = new SocketId();
        socketId.SocketName = sessionId;

        hostPuid = ProductUserId.FromString(hostPuidString);

        clientHandshakeComplete = false;
        helloRetryAccumulator = 0.0;

        GD.Print($"[P2PNetworkManager] StartAsClient sessionId={sessionId} local={localPuidString} host={hostPuidString}");

        // Wyślij od razu pierwszy HELLO
        SendClientHello();
    }

    // (Opcjonalnie) jeśli host ma dynamicznie dopinać graczy po starcie:
    /// <summary>
    /// Registers a new client on the host side dynamically.
    /// </summary>
    /// <param name="clientPuidString">The client's Product User ID string.</param>
    public void HostRegisterClient(string clientPuidString)
    {
        if (!started || !isHost) return;
        if (string.IsNullOrEmpty(clientPuidString)) return;

        if (!hostClients.ContainsKey(clientPuidString))
        {
            var puid = ProductUserId.FromString(clientPuidString);
            if (puid != null && puid.IsValid())
            {
                hostClients[clientPuidString] = puid;
                GD.Print($"[P2PNetworkManager] HostRegisterClient {clientPuidString}");

                // od razu "otwieramy" socket w EOS
                SendRaw(puid, MsgHostPing);
            }
        }
    }

    /// <summary>
    /// Ensures the P2P interface and local PUID are valid.
    /// </summary>
    /// <param name="localPuidString">The local PUID string to validate.</param>
    /// <returns>True if ready; otherwise, false.</returns>
    private bool EnsureReady(string localPuidString)
    {
        if (p2pInterface == null)
        {
            GD.PrintErr("[P2PNetworkManager] ❌ P2PInterface not ready yet");
            return false;
        }

        localPuid = ProductUserId.FromString(localPuidString);
        if (localPuid == null || !localPuid.IsValid())
        {
            GD.PrintErr("[P2PNetworkManager] ❌ Local PUID invalid");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Sends a HELLO message to the host.
    /// </summary>
    private void SendClientHello()
    {
        if (hostPuid == null || !hostPuid.IsValid()) return;

        GD.Print($"[P2PNetworkManager] -> HELLO to host={hostPuid} socket={sessionId}");
        SendRaw(hostPuid, MsgClientHello);
    }

    // ============================================================
    //  RPC SEND (OPCJA B) - GENERYCZNE METODY
    // ============================================================

    /// <summary>
    /// Sends an RPC message to the host.
    /// </summary>
    /// <param name="type">The type of the RPC command.</param>
    /// <param name="payloadObj">The payload object to serialize and send.</param>
    /// <returns>True if sent successfully; otherwise, false.</returns>
    public bool SendRpcToHost(string type, object payloadObj)
    {
        if (!started || isHost) return false;
        if (!clientHandshakeComplete) return false;
        if (hostPuid == null || !hostPuid.IsValid()) return false;

        string json = BuildRpcJson(type, payloadObj);
        SendRaw(hostPuid, json);
        return true;
    }

    /// <summary>
    /// Sends an RPC message to a specific peer.
    /// </summary>
    /// <param name="peer">The target peer's Product User ID.</param>
    /// <param name="type">The type of the RPC command.</param>
    /// <param name="payloadObj">The payload object to serialize and send.</param>
    /// <returns>True if sent successfully; otherwise, false.</returns>
    public bool SendRpcToPeer(ProductUserId peer, string type, object payloadObj)
    {
        if (!started) return false;
        if (peer == null || !peer.IsValid()) return false;

        string json = BuildRpcJson(type, payloadObj);
        SendRaw(peer, json);
        return true;
    }

    /// <summary>
    /// Sends an RPC message to all connected clients (Host only).
    /// </summary>
    /// <param name="type">The type of the RPC command.</param>
    /// <param name="payloadObj">The payload object to serialize and send.</param>
    /// <returns>The number of clients the message was sent to.</returns>
    public int SendRpcToAllClients(string type, object payloadObj)
    {
        if (!started || !isHost) return 0;

        string json = BuildRpcJson(type, payloadObj);
        int sent = 0;

        foreach (var kv in hostClients)
        {
            var peer = kv.Value;
            if (peer != null && peer.IsValid())
            {
                SendRaw(peer, json);
                sent++;
            }
        }

        return sent;
    }

    /// <summary>
    /// Serializes a payload into a JSON RPC string.
    /// </summary>
    /// <param name="type">The RPC type.</param>
    /// <param name="payloadObj">The payload object.</param>
    /// <returns>The serialized JSON string.</returns>
    private static string BuildRpcJson(string type, object payloadObj)
    {
        var wrapper = new
        {
            kind = "rpc",
            type = type,
            payload = payloadObj
        };

        return JsonSerializer.Serialize(wrapper);
    }

    /// <summary>
    /// Sends the game start signal and payload to all welcomed clients.
    /// </summary>
    private void SendGameStartToWelcomedClients()
    {
        if (!started || !isHost) return;
        if (hostGameStartSent) return;

        hostGameStartSent = true;
        hostStartCountdownActive = false;

        GameStartPayload payload = null;

        // 1) Jeśli host dostarcza payload (np. z name/team/seed) -> użyj
        if (hostBuildGameStartPayload != null)
        {
            try
            {
                payload = hostBuildGameStartPayload.Invoke();
            }
            catch (Exception e)
            {
                GD.PrintErr($"[P2PNetworkManager] HostBuildGameStartPayload exception: {e.Message}");
                payload = null;
            }
        }

        // 2) Fallback: budujemy minimalny payload (index + puid)
        if (payload == null)
        {
            payload = BuildDefaultGameStartPayload();
        }

        // Recipients = tylko welcomed
        var welcomedSorted = new List<string>(hostWelcomedClients);
        welcomedSorted.Sort(StringComparer.Ordinal);

        int sent = 0;
        foreach (string puidStr in welcomedSorted)
        {
            if (!hostClients.TryGetValue(puidStr, out var peer)) continue;
            if (peer == null || !peer.IsValid()) continue;

            bool ok = SendRpcToPeer(peer, "game_start", payload);
            if (ok) sent++;
        }

        GD.Print($"[P2PNetworkManager] HOST sent game_start to welcomed clients: {sent}");
        DispatchLocalRpc("game_start", payload);

    }

    /// <summary>
    /// Builds a default game start payload if none is provided by the host delegate.
    /// </summary>
    /// <returns>A default GameStartPayload.</returns>
    private GameStartPayload BuildDefaultGameStartPayload()
    {
        var welcomedSorted = new List<string>(hostWelcomedClients);
        welcomedSorted.Sort(StringComparer.Ordinal);

        var players = new List<GamePlayer>();

        // index 0 = host
        players.Add(new GamePlayer
        {
            index = 0,
            puid = localPuid != null ? localPuid.ToString() : "",
            name = null,
            team = MainGame.Team.None,
            profileIconPath = null

        });

        int index = 1;
        foreach (string puidStr in welcomedSorted)
        {
            players.Add(new GamePlayer
            {
                index = index,
                puid = puidStr,
                name = null,
                team = MainGame.Team.None,
                profileIconPath = null
            });
            index++;
        }

        return new GameStartPayload
        {
            sessionId = sessionId,
            players = players.ToArray()
        };
    }

    // ============================================================
    //  RECEIVE + ROUTING
    // ============================================================

    /// <summary>
    /// Handles an incoming packet, routing it based on socket and channel.
    /// </summary>
    /// <param name="peer">The sender's Product User ID.</param>
    /// <param name="socketName">The socket name the packet arrived on.</param>
    /// <param name="channel">The channel the packet arrived on.</param>
    /// <param name="payload">The raw packet data.</param>
    private void HandlePacket(ProductUserId peer, string socketName, byte channel, byte[] payload)
    {
        if (socketName != sessionId) return; // ignoruj inne sockety
        if (channel != Channel) return;      // trzymamy się kanału 0

        string msg = Encoding.UTF8.GetString(payload);

        if (isHost)
        {
            // Host dostaje HELLO -> odsyła WELCOME
            if (msg == MsgClientHello)
            {
                string peerStr = peer.ToString();

                if (!hostWelcomedClients.Contains(peerStr))
                {
                    GD.Print($"[P2PNetworkManager] HOST <- HELLO from {peerStr} (sending WELCOME)");
                    hostWelcomedClients.Add(peerStr);
                }
                else
                {
                    GD.Print($"[P2PNetworkManager] HOST <- HELLO again from {peerStr} (re-sending WELCOME)");
                }

                SendRaw(peer, MsgHostWelcome);
                return;
            }

            if (msg == MsgHostPing)
            {
                // nieistotne dla hosta
                return;
            }

            // JSON RPC -> do handlerów
            if (TryDispatchJsonRpc(msg, peer))
                return;

            GD.Print($"[P2PNetworkManager] HOST <- {msg} from {peer}");
        }
        else
        {
            // Client dostaje WELCOME -> koniec retry HELLO
            if (msg == MsgHostWelcome)
            {
                if (clientHandshakeComplete) return; // ignoruj duplikat welcome
                clientHandshakeComplete = true;
                GD.Print($"[P2PNetworkManager] CLIENT <- WELCOME (handshake done)");

                // === HANDSHAKE EVENT (DODANE) ===
                FireHandshakeCompletedOnce();
                // ===============================

                return;
            }

            if (msg == MsgHostPing)
            {
                // host ping - można zignorować
                return;
            }

            // JSON RPC -> do handlerów
            if (TryDispatchJsonRpc(msg, peer))
                return;

            GD.Print($"[P2PNetworkManager] CLIENT <- {msg} from {peer}");
        }
    }

    /// <summary>
    /// Attempts to parse and dispatch a JSON RPC message.
    /// </summary>
    /// <param name="msg">The raw message string.</param>
    /// <param name="fromPeer">The sender's Product User ID.</param>
    /// <returns>True if the message was parsed and dispatched (even if no handler processed it); otherwise, false.</returns>
    private bool TryDispatchJsonRpc(string msg, ProductUserId fromPeer)
    {
        // szybki filtr, żeby nie próbować parsować zwykłych stringów
        if (string.IsNullOrEmpty(msg) || msg[0] != '{')
            return false;

        NetMessage parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<NetMessage>(msg);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[P2PNetworkManager] JSON parse error: {e.Message}");
            return false;
        }

        if (parsed == null) return false;
        if (parsed.kind != "rpc") return false;

        // (4) Opcja 2: bez bufora.
        // Jeśli nikt nie słucha -> nie ma co robić
        if (PacketHandlers == null)
        {
            GD.Print($"[P2PNetworkManager] RPC dropped (no handlers): type={parsed.type} from={fromPeer}");
            return true; // "zjedzone" żeby nie spamować fallback logiem
        }

        bool consumed = false;

        foreach (PacketHandler handler in PacketHandlers.GetInvocationList())
        {
            try
            {
                if (handler(parsed, fromPeer))
                {
                    consumed = true;
                    break;
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"[P2PNetworkManager] Handler exception for type={parsed.type}: {e.Message}");
                GD.PrintErr(e.StackTrace);
            }
        }

        if (!consumed)
        {
            GD.Print($"[P2PNetworkManager] RPC not handled: type={parsed.type} from={fromPeer} json={msg}");
        }

        return true; // JSON zawsze "zjadamy" (obsłużone albo świadomie pominięte)
    }

    /// <summary>
    /// Dispatches a local loopback RPC message.
    /// </summary>
    /// <param name="type">The RPC type.</param>
    /// <param name="payloadObj">The payload object.</param>
    private void DispatchLocalRpc(string type, object payloadObj)
    {
        if (PacketHandlers == null) return;

        var msg = new NetMessage
        {
            kind = "rpc",
            type = type,
            payload = JsonSerializer.SerializeToElement(payloadObj)
        };

        bool consumed = false;

        foreach (PacketHandler handler in PacketHandlers.GetInvocationList())
        {
            try
            {
                if (handler(msg, localPuid))
                {
                    consumed = true;
                    break;
                }
            }
            catch (Exception e)
            {
                GD.PrintErr($"[P2PNetworkManager] Local handler exception for type={type}: {e.Message}");
            }
        }

        if (!consumed)
        {
            GD.Print($"[P2PNetworkManager] Local RPC not handled: type={type}");
        }
    }


    // ============================================================
    //  RAW SEND
    // ============================================================

    /// <summary>
    /// Sends a raw string message to a remote peer.
    /// </summary>
    /// <param name="remote">The target peer's Product User ID.</param>
    /// <param name="text">The text message to send.</param>
    private void SendRaw(ProductUserId remote, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);

        var options = new SendPacketOptions
        {
            LocalUserId = localPuid,
            RemoteUserId = remote,
            SocketId = socketId,
            Channel = Channel,
            Data = new ArraySegment<byte>(bytes),
            AllowDelayedDelivery = true,
            Reliability = PacketReliability.ReliableOrdered,
            DisableAutoAcceptConnection = false
        };

        Result r = p2pInterface.SendPacket(ref options);
        if (r != Result.Success)
        {
            GD.PrintErr($"[P2PNetworkManager] SendPacket failed: {r} (to={remote} msg={text})");
        }
    }

    // ============================================================
    //  RECEIVE (EOS)
    // ============================================================

    /// <summary>
    /// Structure holding received packet data.
    /// </summary>
    private struct ReceivedPacket
    {
        public ProductUserId peerPuid;
        public string socketName;
        public byte channel;
        public byte[] payload;
    }

    /// <summary>
    /// Attempts to receive the next available packet from the P2P interface.
    /// </summary>
    /// <param name="packet">The received packet info, if successful.</param>
    /// <returns>True if a packet was successfully received; otherwise, false.</returns>
    private bool TryReceivePacket(out ReceivedPacket packet)
    {
        packet = default;

        // 1) Jaki rozmiar ma następny pakiet?
        var sizeOptions = new GetNextReceivedPacketSizeOptions
        {
            LocalUserId = localPuid,
            RequestedChannel = null
        };

        uint nextSize = 0;
        Result sizeResult = p2pInterface.GetNextReceivedPacketSize(ref sizeOptions, out nextSize);

        if (sizeResult == Result.NotFound)
            return false;

        if (sizeResult != Result.Success)
        {
            GD.PrintErr($"[P2PNetworkManager] GetNextReceivedPacketSize failed: {sizeResult}");
            return false;
        }

        if (nextSize == 0)
            return false;

        byte[] outData = new byte[nextSize];

        var recvOptions = new ReceivePacketOptions
        {
            LocalUserId = localPuid,
            MaxDataSizeBytes = nextSize,
            RequestedChannel = null
        };

        ProductUserId outPeerId = null;
        SocketId outSocket = new SocketId();
        byte outChannel;
        uint outBytesWritten;

        Result recvResult = p2pInterface.ReceivePacket(
            ref recvOptions,
            ref outPeerId,
            ref outSocket,
            out outChannel,
            new ArraySegment<byte>(outData),
            out outBytesWritten
        );

        if (recvResult == Result.NotFound)
            return false;

        if (recvResult != Result.Success)
        {
            GD.PrintErr($"[P2PNetworkManager] ReceivePacket failed: {recvResult}");
            return false;
        }

        if (outBytesWritten == 0)
            return false;

        byte[] trimmed = new byte[outBytesWritten];
        Buffer.BlockCopy(outData, 0, trimmed, 0, (int)outBytesWritten);

        packet = new ReceivedPacket
        {
            peerPuid = outPeerId,
            socketName = outSocket.SocketName,
            channel = outChannel,
            payload = trimmed
        };

        return true;
    }
}
