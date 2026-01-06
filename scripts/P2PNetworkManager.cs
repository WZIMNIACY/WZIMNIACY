// P2PNetworkManager.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;

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

    // === JSON RPC (PUBLICZNE MODELE) ===
    public sealed class NetMessage
    {
        public string kind { get; set; }    // "rpc"
        public string type { get; set; }    // np. "card_selected"
        public JsonElement payload { get; set; } // dowolny obiekt payload
    }

    // === HANDLERY PAKIETÓW (router) ===
    // Handler zwraca true jeśli "zjada" pakiet (czyli był obsłużony)
    public delegate bool PacketHandler(NetMessage packet, ProductUserId fromPeer);

    public event PacketHandler PacketHandlers;

    // --- Przydatne property (do użycia w MainGame / innych plikach) ---
    public bool IsHost => isHost;
    public bool Started => started;
    public bool ClientHandshakeComplete => clientHandshakeComplete; // tylko sensowne po stronie klienta
    public string SessionId => sessionId;
    public ProductUserId LocalPuid => localPuid;
    public ProductUserId HostPuid => hostPuid;

    // === HANDSHAKE EVENT (DODANE) ===
    public event Action HandshakeCompleted;

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
    }

    // HOST start
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

    private void SendClientHello()
    {
        if (hostPuid == null || !hostPuid.IsValid()) return;

        GD.Print($"[P2PNetworkManager] -> HELLO to host={hostPuid} socket={sessionId}");
        SendRaw(hostPuid, MsgClientHello);
    }

    // ============================================================
    //  RPC SEND (OPCJA B) - GENERYCZNE METODY
    // ============================================================

    public bool SendRpcToHost(string type, object payloadObj)
    {
        if (!started || isHost) return false;
        if (!clientHandshakeComplete) return false;
        if (hostPuid == null || !hostPuid.IsValid()) return false;

        string json = BuildRpcJson(type, payloadObj);
        SendRaw(hostPuid, json);
        return true;
    }

    public bool SendRpcToPeer(ProductUserId peer, string type, object payloadObj)
    {
        if (!started) return false;
        if (peer == null || !peer.IsValid()) return false;

        string json = BuildRpcJson(type, payloadObj);
        SendRaw(peer, json);
        return true;
    }

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

    // ============================================================
    //  RECEIVE + ROUTING
    // ============================================================

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
            }
        }

        if (!consumed)
        {
            GD.Print($"[P2PNetworkManager] RPC not handled: type={parsed.type} from={fromPeer} json={msg}");
        }

        return true; // JSON zawsze "zjadamy" (obsłużone albo świadomie pominięte)
    }

    // ============================================================
    //  RAW SEND
    // ============================================================

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

    private struct ReceivedPacket
    {
        public ProductUserId peerPuid;
        public string socketName;
        public byte channel;
        public byte[] payload;
    }

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
