using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Threading.Tasks;
using HoundNetwork.NetworkModels;
using System.IO;

namespace HoundNetwork
{
    public class HoundServer
    {
        private IPAddress _ipAddress;
        private int _port;
        private TcpListener _tcpListener;
        private ConcurrentDictionary<Guid, ClientInfo> _connectedClients;

        public HoundServer(string ipAddress, int port)
        {
            _ipAddress = IPAddress.Parse(ipAddress);
            _port = port;
            _tcpListener = new TcpListener(_ipAddress, _port);
            _connectedClients = new ConcurrentDictionary<Guid, ClientInfo>();
        }
        public async Task StartAsync()
        {
            _tcpListener.Start();

            while (true)
            {
                TcpClient client = await _tcpListener.AcceptTcpClientAsync();
                var _client = new ClientInfo
                {
                    Guid = Guid.NewGuid(),
                    Client = client
                };
                _connectedClients.TryAdd(_client.Guid, _client);
                _ = Task.Run(async () => await HandleClientAsync(_client));
                _ = Task.Run(async () => await RegistrationSubscribe(_client));
                _ = Task.Run(async () => await KeepAliveSubscribe(_client));
            }
        }

        private async Task HandleClientAsync(ClientInfo Client)
        {
            byte[] buffer = new byte[8012];
            Dictionary<Guid, List<NetworkPacket>> receivedFragments = new Dictionary<Guid, List<NetworkPacket>>();

            while (Client.Client.Connected)
            {
                try
                {
                    Client.GetCancellationTokenSource().Token.ThrowIfCancellationRequested();
                    NetworkStream stream = Client.Client.GetStream();
                    (bool, string) resultDeserialize = (true, "");

                    using (MemoryStream ms = new MemoryStream())
                    {
                        int bytesRead;
                        int bytesToRead = -1;

                        while (true)
                        {
                            bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, Client.GetCancellationTokenSource().Token);

                            if (bytesRead > 0)
                            {
                                ms.Write(buffer, 0, bytesRead);

                                if (ms.Length >= bytesToRead && bytesToRead == -1)
                                {
                                    byte[] data = ms.ToArray();
                                    try
                                    {
                                        resultDeserialize = NetworkSerialization.TryDeserialize(data, out NetworkPacket payload);
                                        if (resultDeserialize.Item1 && int.TryParse(payload.FragmentIndex.ToString(), out int payres))
                                        {
                                            if(payload.FragmentIndex == payres)
                                            {
                                                await CombineFragmentFromDictonary(Client, receivedFragments, payload);
                                                await SendAcknowledgmentAsync(Client, payload.PacketId, payload.FragmentIndex);
                                            }
                                        }

                                        ms.SetLength(0);
                                        bytesToRead = -1;
                                    }
                                    catch (IOException e)
                                    {
                                        Console.WriteLine($"Ошибка при чтении данных с клиента: {e.Message}.");
                                        bytesToRead += 1;
                                        return;
                                    }
                                    catch (ObjectDisposedException)
                                    {
                                        Console.WriteLine(resultDeserialize.Item2);
                                        bytesToRead += 1;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Сервер: ошибка обработки данных:\n{e.Message}\n{e.StackTrace}");
                }
            }
        }
        private async Task RegistrationSubscribe(ClientInfo client)
        {
            using (var registrationSubscribe = client.MessageQueue.Subscribe(TypePacket.Registration, async payload =>
            {
                string displayName = (string)payload.ObjectData;

                Console.WriteLine($"Регистрация клиента: {displayName} : {client.Guid}");
                _connectedClients.TryGetValue(client.Guid, out ClientInfo clientInfo);
                clientInfo.DisplayName = displayName;
                clientInfo.SetRegistration();
                payload = new NetworkPayload
                {
                    ObjectData = client.Guid,
                    PacketType = TypePacket.Registration,
                    Receiver = client.Guid,
                    Sender = Guid.Empty
                };
                await SendDataAsync(payload);
            }))
            {
                await Task.Delay(100);
            }
        }
        private async Task KeepAliveSubscribe(ClientInfo client)
        {
            using (var disconectSubscribe = client.MessageQueue.Subscribe(TypePacket.KeepAlive, payload =>
            {
                Console.WriteLine($"{client.DisplayName}: Я жив!");
                _connectedClients[client.Guid].UpdateLastActivityTime();
            }))
            {
                while (!client.GetCancellationTokenSource().Token.IsCancellationRequested)
                {
                    await Task.Delay(20);
                }
            }
        }
        
        private async Task CombineFragmentFromDictonary(ClientInfo client, Dictionary<Guid, List<NetworkPacket>> receivedFragments, NetworkPacket payload)
        {
            if (payload.TotalFragments > 1)
            {
                if (!receivedFragments.ContainsKey(payload.PacketId))
                {
                    receivedFragments[payload.PacketId] = new List<NetworkPacket>();
                }

                receivedFragments[payload.PacketId].Add(payload);
                Console.WriteLine($"Прием {receivedFragments[payload.PacketId].Count} из {payload.TotalFragments}");

                if (receivedFragments[payload.PacketId].Count == payload.TotalFragments)
                {
                    var combinedPayload = NetworkSerialization.CombineFragments(receivedFragments[payload.PacketId]);

                    await client.MessageQueue.EnqueueAsync(combinedPayload);

                    receivedFragments.Remove(payload.PacketId);
                }
            }
            else
            {
                NetworkSerialization.Deserialize(payload.Data, out object onePayload);
                await client.MessageQueue.EnqueueAsync((NetworkPayload)onePayload);
            }
        }
        public async Task SendDataAsync(NetworkPayload payload)
        {
            var client = _connectedClients[payload.Receiver];

            if (client.Client.Connected)
            {
                var serializedData = NetworkSerialization.PreparingForSend(payload).ToList();
                var guidBytes = new byte[16];
                Array.Copy(serializedData[0], guidBytes, 16);
                var packetID = new Guid(guidBytes);

                for (int i = 0; i < serializedData.Count; i++)
                {
                    try
                    {
                        NetworkStream stream = client.Client.GetStream();
                        byte[] byteItem = serializedData[i];
                        await stream.WriteAsync(byteItem, 0, byteItem.Length);
                        using (var messageSubscription = client.MessageQueue.Subscribe(TypePacket.PacketCallback, packet =>
                        {
                            var cb = (Callback)packet.ObjectData;
                            try
                            {
                                if (packetID == cb.Guid)
                                {
                                    if (cb.FragmentPacket != i)
                                    {
                                        i = cb.FragmentPacket - 1;
                                    }
                                }
                            }
                            catch
                            {
                                Console.WriteLine($"Ошибка при получении подтверждения: некорректное значение ACK");
                            }
                        }))
                        {
                            await Task.Delay(5);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Ошибка отправки данных: {e.Message}");
                    }
                }
            }
        }
        private async Task SendAcknowledgmentAsync(ClientInfo client, Guid packetId, int fragmentIndex)
        {
            var callbackPacket = new Callback
            {
                Guid = packetId,
                FragmentPacket = fragmentIndex
            };
            NetworkPayload payload = new NetworkPayload(Guid.Empty, client.Guid, TypePacket.PacketCallback, callbackPacket);
            await SendDataAsync(payload);
        }
        private void DisconnectClient(Guid clientId)
        {
            if (_connectedClients.TryRemove(clientId, out ClientInfo clientInfo))
            {
                clientInfo.GetCancellationTokenSource().Cancel();

                Task.Delay(100).ContinueWith(_ => clientInfo.Client.Close());
            }
        }
        private async void DisconnectInactiveClients(object state)
        {
            DateTime now = DateTime.Now;

            foreach (var clientId in _connectedClients.Keys)
            {
                if (now - _connectedClients[clientId].GetLastActivityTime() > TimeSpan.FromMinutes(1))
                {
                    NetworkPayload disconnectPayload = new NetworkPayload
                    {
                        PacketType = TypePacket.ClientDisconnect,
                        Receiver = clientId,
                        ObjectData = null,
                    };
                    await SendDataAsync(disconnectPayload);

                    if (_connectedClients.TryGetValue(clientId, out ClientInfo clientInfo))
                    {
                        if (clientInfo.GetCancellationTokenSource() != null)
                        {
                            DisconnectClient(clientId);
                            _connectedClients.TryRemove(clientId, out _);
                        }
                    }
                }
            }
        }
    }
}
