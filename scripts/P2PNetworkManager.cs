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


    // Handshake messages
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

    // === JSON RPC (DODANE) ===
    private sealed class NetMessage
    {
        public string kind { get; set; }   // "rpc"
        public string type { get; set; }   // np. "card_selected"
        public JsonElement payload { get; set; } // dowolny obiekt payload
    }

    private sealed class CardSelectedPayload
    {
        public int cardId { get; set; }
        public string by { get; set; }     // np. localPuid.ToString()
    }
    // =========================

    // === JSON TEST (DODANE) ===
    private bool jsonTestSent = false;           // klient wyśle tylko raz po handshake
    private int jsonTestTick = 0;                // licznik testów na hoście
    // ==========================

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

        // === JSON TEST (DODANE) ===
        jsonTestSent = false;
        // ==========================

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

    // === JSON RPC (DODANE) ===
    public void SendCardSelectedToHost(int cardId)
    {
        if (!started || isHost) return;
        if (!clientHandshakeComplete) return;
        if (hostPuid == null || !hostPuid.IsValid()) return;

        var payload = new CardSelectedPayload
        {
            cardId = cardId,
            by = localPuid.ToString()
        };

        var wrapper = new
        {
            kind = "rpc",
            type = "card_selected",
            payload = payload
        };

        SendRaw(hostPuid, JsonSerializer.Serialize(wrapper));
    }
    // =========================

    private void HandlePacket(ProductUserId peer, string socketName, byte channel, byte[] payload)
    {
        if (socketName != sessionId) return;     // ignoruj inne sockety
        if (channel != Channel) return;          // trzymamy się kanału 0

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

            // === JSON RPC (DODANE) ===
            if (msg.Length > 0 && msg[0] == '{')
            {
                if (TryHandleJsonRpcHost(peer, msg))
                    return;
            }
            // =========================

            // Tu później podepniesz RPC gameplay (Twoje message types)
            GD.Print($"[P2PNetworkManager] HOST <- {msg} from {peer}");
        }
        else
        {
            // Client dostaje WELCOME -> koniec retry HELLO
            if (msg == MsgHostWelcome)
            {
                if (clientHandshakeComplete) return;   // ignoruj duplikat welcome
                clientHandshakeComplete = true;
                GD.Print($"[P2PNetworkManager] CLIENT <- WELCOME (handshake done)");

                // === JSON TEST (DODANE) ===
                if (!jsonTestSent)
                {
                    jsonTestSent = true;
                    int testCardId = 777;
                    GD.Print($"[P2PNetworkManager] JSON_TEST CLIENT -> SendCardSelectedToHost cardId={testCardId}");
                    SendCardSelectedToHost(testCardId);
                }
                // ==========================

                return;
            }

            if (msg == MsgHostPing)
            {
                // host ping - można zignorować
                return;
            }

            // === JSON TEST (DODANE) ===
            if (msg.Length > 0 && msg[0] == '{')
            {
                if (TryHandleJsonRpcClient(peer, msg))
                    return;
            }
            // ==========================

            GD.Print($"[P2PNetworkManager] CLIENT <- {msg} from {peer}");
        }
    }

    // === JSON RPC (DODANE) ===
    private bool TryHandleJsonRpcHost(ProductUserId peer, string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<NetMessage>(json);
            if (parsed == null) return false;
            if (parsed.kind != "rpc") return false;

            if (parsed.type == "card_selected")
            {
                var payload = parsed.payload.Deserialize<CardSelectedPayload>();
                GD.Print($"[P2PNetworkManager] HOST <- RPC card_selected cardId={payload.cardId} by={payload.by} from={peer}");

                // === JSON TEST (DODANE) ===
                jsonTestTick++;
                GD.Print($"[P2PNetworkManager] JSON_TEST HOST OK #{jsonTestTick} (received card_selected)");

                var ack = new
                {
                    kind = "rpc",
                    type = "json_test_ack",
                    payload = new { ok = true, tick = jsonTestTick, gotCardId = payload.cardId }
                };

                string ackJson = JsonSerializer.Serialize(ack);
                GD.Print($"[P2PNetworkManager] JSON_TEST HOST -> send json_test_ack to={peer} json={ackJson}");
                SendRaw(peer, ackJson);
                // ==========================

                // TODO: tutaj wywołasz logikę gry u hosta, np. MainGame.OnCardSelected(...)
                return true;
            }

            // inne typy RPC w przyszłości
            return false;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[P2PNetworkManager] HOST JSON parse error: {e.Message}");
            return false;
        }
    }
    // =========================

    // === JSON TEST (DODANE) ===
    private bool TryHandleJsonRpcClient(ProductUserId peer, string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<NetMessage>(json);
            if (parsed == null) return false;
            if (parsed.kind != "rpc") return false;

            if (parsed.type == "json_test_ack")
            {
                GD.Print($"[P2PNetworkManager] JSON_TEST CLIENT <- ACK from={peer} json={json}");
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[P2PNetworkManager] CLIENT JSON parse error: {e.Message}");
            return false;
        }
    }
    // ==========================

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
