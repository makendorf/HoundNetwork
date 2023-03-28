using HoundNetwork.Properties;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HoundNetwork.NetworkModels
{
    public class ClientInfo : NetworkInteractions
    {
        private TcpClient _client;

        public TcpClient Client { get => _client; set { _client = value; } }
        public Guid ServerGuid = Credentions.Default.ServerGuid;
        public ClientInfo(string displayname)
        {
            Client = new TcpClient();
            DisplayName = displayname;
        }
        public ClientInfo()
        {
        }
        public async Task ConnectAsync(int timeoutMs = 5000)
        {
            try
            {
                var connectTask = Client.ConnectAsync(ServerAddress, Port);
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask)
                {
                    throw new TimeoutException("Тайм-аут соединения.");
                }

                await connectTask;

                _ = Task.Run(async () => await ReceiveDataAsync(this));

                _ = Task.Run(async () => await PeriodicConnectionCheckAsync(TimeSpan.FromMinutes(5)));

                _ = Task.Run(async () => await RegistrationSubscribe());

                _ = Task.Run(async () => await SendKeepAliveAsync(TimeSpan.FromSeconds(5)));

                _ = Task.Run(async () => await DisconectSubscribe());

                //_ = Task.Run(async () => await CheckCancelationToken());
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
        private async Task PeriodicConnectionCheckAsync(TimeSpan interval)
        {
            while (!GetCancellationTokenSource().IsCancellationRequested)
            {
                try
                {
                    if (!Client.Client.Connected)
                    {
                        Console.WriteLine("Отключение от сервера. Попытка переподключения...");
                        await ReconnectAsync();
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Ошибка переподключения: {e.Message}");
                }

                await Task.Delay(interval);
            }
        }
        private async Task ReconnectAsync()
        {
            int retryDelayMs = 1000;
            int maxRetryDelayMs = 60000;

            while (!Client.Client.Connected || GetCancellationTokenSource().Token.IsCancellationRequested)
            {
                try
                {
                    await Client.ConnectAsync(ServerAddress, Port);
                }
                catch
                {
                    await Task.Delay(retryDelayMs);
                    retryDelayMs = Math.Min(retryDelayMs * 2, maxRetryDelayMs);
                }
            }
        }
        private async Task SendKeepAliveAsync(TimeSpan interval)
        {
            if (GetRegistration())
            {
                while (!GetCancellationTokenSource().IsCancellationRequested)
                {
                    NetworkPayload keepAlivePayload = new NetworkPayload
                    {
                        PacketType = TypePacket.KeepAlive,
                        Sender = GUID,
                        ObjectData = Array.Empty<byte>()
                    };

                    await SendDataAsync(this, keepAlivePayload);

                    await Task.Delay(interval);
                }
            }
        }
        private async Task RegistrationSubscribe()
        {
            Guid clientId = Guid.NewGuid();
            NetworkPayload registrationPayload = new NetworkPayload(clientId, Guid.Empty, TypePacket.Registration, DisplayName);
            var response = await SendDataAndWaitForResponseAsync(registrationPayload);
            var guid = (Guid)response.ObjectData;
            Console.WriteLine($"Смена GUID: {GUID} -> {guid}");
            SetRegistration(true);
            GUID = guid;
            await SendDataAsync(this, registrationPayload);
        }
        private async Task DisconectSubscribe()
        {
            var disconectPacket = await WaitForPacketAsync(TypePacket.ClientDisconnect);
            Console.WriteLine("Завершение работы");
            GetCancellationTokenSource().Cancel();
        }
        public async Task<NetworkPayload> SendDataAndWaitForResponseAsync(NetworkPayload payload, int timeout = 5000)
        {
            return await SendDataAndWaitForResponseAsync(this, payload, timeout);
        }
        public async Task<NetworkPayload> SendDataAndWaitForResponseAsync(NetworkPayload payload, TypePacket typeResponse, int timeout = 5000)
        {
            return await SendDataAndWaitForResponseAsync(this, payload, typeResponse, timeout);
        }
        public async Task<NetworkPayload> WaitForPacketAsync(TypePacket packetType)
        {
            return await WaitForPacketAsync(this, packetType);
        }
        public async Task<NetworkPayload> WaitForPacketAsync(TypePacket packetType, int timeout = 5000)
        {
            return await WaitForPacketAsync(this, packetType, timeout);
        }
    }
    public class ServerInfo : NetworkInteractions
    {
        private TcpListener _tcpListener;
        public ConcurrentDictionary<Guid, ClientInfo> _connectedClients { get; private set; } = new ConcurrentDictionary<Guid, ClientInfo>();
        public TcpListener TcpListener { get => _tcpListener; private set { _tcpListener = value; } } 
        public ServerInfo(string displayname) 
        {
            _tcpListener = new TcpListener(IPAddress.Parse(ServerAddress), Port);
            DisplayName = displayname;
        }
        public async Task StartAsync()
        {
            _tcpListener.Start();

            while (true)
            {
                TcpClient client = await _tcpListener.AcceptTcpClientAsync();
                var _client = new ClientInfo
                {
                    Client = client
                };
                _connectedClients.TryAdd(Guid.NewGuid(), _client);

                _ = Task.Run(async () => await ReceiveDataAsync(_client));

                _ = Task.Run(async () => await RegistrationSubscribe(_client));

                _ = Task.Run(async () => await KeepAliveSubscribe(_client));
            }
        }
        private async Task RegistrationSubscribe(ClientInfo client)
        {
            var payload = await client.WaitForPacketAsync(client, TypePacket.Registration);
            string displayName = (string)payload.ObjectData;

            Console.WriteLine($"Регистрация клиента: {displayName} : {client.GUID}");
            _connectedClients.TryGetValue(client.GUID, out ClientInfo clientInfo);
            clientInfo.DisplayName = displayName;
            clientInfo.SetRegistration(true);
            var response = new NetworkPayload
            {
                ObjectData = client.GUID,
                PacketType = TypePacket.Registration,
                Receiver = client.GUID,
                Sender = Guid.Empty
            };
            await SendDataAsync(client, response);
        }
        private async Task KeepAliveSubscribe(ClientInfo client)
        {
            while (!client.GetCancellationTokenSource().Token.IsCancellationRequested)
            {
                await WaitForPacketAsync(client, TypePacket.KeepAlive);
            }
        }
    }
    public class NetworkInteractions
    {
        private string _displayname;
        private bool _registration;

        public int Port { get { return Credentions.Default.ServerPort; } }
        public string ServerAddress { get { return Credentions.Default.ServerIP; } }
        public string DisplayName { get { return _displayname; } set { _displayname = value; } }
        public Guid GUID { get { return Credentions.Default.ServerGuid; } set { GUID = value; } }
        private bool Registration { get { return _registration; } set { _registration = value; } }
        private CancellationTokenSource CancellationTokenSource { get; set; } = new CancellationTokenSource();
        private PacketSubscribe PacketQueue { get; } = new PacketSubscribe();

        public async Task ReceiveDataAsync(ClientInfo Client)
        {
            byte[] buffer = new byte[8012];
            Dictionary<Guid, List<NetworkPacket>> receivedFragments = new Dictionary<Guid, List<NetworkPacket>>();

            while (Client.Client.Connected)
            {
                try
                {
                    GetCancellationTokenSource().Token.ThrowIfCancellationRequested();
                    NetworkStream stream = Client.Client.GetStream();
                    (bool, string) resultDeserialize = (true, "");

                    using (MemoryStream ms = new MemoryStream())
                    {
                        int bytesRead;
                        int bytesToRead = -1;

                        while (true)
                        {
                            bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, GetCancellationTokenSource().Token);

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
                                            if (payload.FragmentIndex == payres)
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
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Операция была отменена.");

                }
                catch (Exception e)
                {
                    Console.WriteLine($"Клиент: ошибка обработки данных:\n{e.Message}\n{e.StackTrace}");
                }
            }
        }
        private async Task CombineFragmentFromDictonary(ClientInfo Client, Dictionary<Guid, List<NetworkPacket>> receivedFragments, NetworkPacket payload)
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

                    await Client.PacketQueue.EnqueueAsync(combinedPayload);

                    receivedFragments.Remove(payload.PacketId);
                }
            }
            else
            {
                NetworkSerialization.Deserialize(payload.Data, out object onePayload);
                await Client.PacketQueue.EnqueueAsync((NetworkPayload)onePayload);
            }
        }
        private async Task SendAcknowledgmentAsync(ClientInfo client, Guid packetId, int fragmentIndex)
        {
            var callbackPacket = new Callback
            {
                Guid = packetId,
                FragmentPacket = fragmentIndex
            };
            NetworkPayload payload = new NetworkPayload(Guid.Empty, client.GUID, TypePacket.Acknowledgment, callbackPacket);
            await SendDataAsync(client, payload);
        }
        public async Task SendDataAsync(ClientInfo client, NetworkPayload payload, int timeout = 5000)
        {
            if (client.Client.Connected)
            {
                var serializedData = NetworkSerialization.PreparingForSend(payload).ToList();
                var guidBytes = new byte[16];
                Array.Copy(serializedData[0], guidBytes, 16);
                var packetID = new Guid(guidBytes);
                if (serializedData.Count > 1)
                {
                    for (int i = 0; i < serializedData.Count; i++)
                    {
                        try
                        {
                            NetworkStream stream = client.Client.GetStream();
                            byte[] byteItem = serializedData[i];
                            await stream.WriteAsync(byteItem, 0, byteItem.Length);
                            var ack = await WaitForPacketAsync(client, TypePacket.Acknowledgment, timeout);
                            var cb = (Callback)(await WaitForPacketAsync(client, TypePacket.Acknowledgment, timeout)).ObjectData;
                            try
                            {
                                if ((ack.PacketType == TypePacket.Acknowledgment) && (packetID == cb.Guid))
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
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Ошибка отправки данных: {e.Message}");
                        }
                    }
                }
                else
                {
                    NetworkStream stream = client.Client.GetStream();
                    byte[] byteItem = serializedData[0];
                    await stream.WriteAsync(byteItem, 0, byteItem.Length);
                }
            }
        }
        public PacketSubscribe GetPacketQueue()
        {
            return PacketQueue;
        }
        public void SetRegistration(bool value)
        {
            Registration = value;
        }
        public bool GetRegistration()
        {
            return Registration;
        }
        public void UpdateCancellationTokenSource()
        {
            CancellationTokenSource = new CancellationTokenSource();
        }
        public CancellationTokenSource GetCancellationTokenSource()
        {
            return CancellationTokenSource;
        }
        public async Task<NetworkPayload> SendDataAndWaitForResponseAsync(ClientInfo client, NetworkPayload payload, int timeout = 5000)
        {
            NetworkPayload responsePayload = null;
            var responseReceived = new TaskCompletionSource<bool>();

            void OnResponseReceived(NetworkPayload receivedPayload)
            {
                if (client.Client.Connected)
                {
                    responsePayload = receivedPayload;
                    responseReceived.TrySetResult(true);
                }
            }

            using (var responseSubscription = client.PacketQueue.Subscribe(payload.PacketType, OnResponseReceived))
            {
                await SendDataAsync(client, payload);

                if (await Task.WhenAny(responseReceived.Task, Task.Delay(timeout)) == responseReceived.Task)
                {
                    return responsePayload;
                }
                else
                {
                    throw new TimeoutException($"Тайм-аут ожидания ответного пакета. ({timeout} мс)");
                }
            }
        }
        public async Task<NetworkPayload> SendDataAndWaitForResponseAsync(ClientInfo client, NetworkPayload payload, TypePacket typeResponse, int timeout = 5000)
        {
            NetworkPayload responsePayload = null;
            var responseReceived = new TaskCompletionSource<bool>();

            void OnResponseReceived(NetworkPayload receivedPayload)
            {
                if (client.Client.Connected)
                {
                    responsePayload = receivedPayload;
                    responseReceived.TrySetResult(true);
                }
            }

            using (var responseSubscription = client.PacketQueue.Subscribe(typeResponse, OnResponseReceived))
            {
                await SendDataAsync(client, payload);

                if (await Task.WhenAny(responseReceived.Task, Task.Delay(timeout)) == responseReceived.Task)
                {
                    return responsePayload;
                }
                else
                {
                    throw new TimeoutException($"Тайм-аут ожидания ответного пакета. ({timeout} мс)");
                }
            }
        }
        public async Task<Subscription> SendDataAndSubscribeAsync(ClientInfo client, NetworkPayload payload, TypePacket responsePacketType, Action<NetworkPayload> onResponseReceived)
        {
            await SendDataAsync(client, payload);

            return client.PacketQueue.Subscribe(responsePacketType, onResponseReceived);
        }
        public async Task<Subscription> SendDataAndSubscribeAsync(ClientInfo client, NetworkPayload payload, Action<NetworkPayload> onResponseReceived)
        {
            await SendDataAsync(client, payload);

            return client.PacketQueue.Subscribe(payload.PacketType, onResponseReceived);
        }
        public async Task<NetworkPayload> WaitForPacketAsync(ClientInfo client, TypePacket packetType)
        {
            NetworkPayload receivedPayload = null;
            var payloadReceived = new TaskCompletionSource<bool>();

            void OnPayloadReceived(NetworkPayload payload)
            {
                receivedPayload = payload;
                payloadReceived.TrySetResult(true);
            }

            using (var payloadSubscription = client.PacketQueue.Subscribe(packetType, OnPayloadReceived))
            {
                await payloadReceived.Task;
                return receivedPayload;
            }
        }
        public async Task<NetworkPayload> WaitForPacketAsync(ClientInfo client, TypePacket packetType, int timeout = 5000)
        {
            NetworkPayload receivedPayload = null;
            var payloadReceived = new TaskCompletionSource<bool>();

            void OnPayloadReceived(NetworkPayload payload)
            {
                receivedPayload = payload;
                payloadReceived.TrySetResult(true);
            }

            using (var payloadSubscription = client.PacketQueue.Subscribe(packetType, OnPayloadReceived))
            {
                if (await Task.WhenAny(payloadReceived.Task, Task.Delay(timeout)) == payloadReceived.Task)
                {
                    return receivedPayload;
                }
                else
                {
                    throw new TimeoutException($"Тайм-аут ожидания ответного пакета. ({timeout} мс)");
                }
            }
        }
        public void Subscribe(ClientInfo client, TypePacket responsePacketType, Action<NetworkPayload> onResponseReceived)
        {
            client.PacketQueue.Subscribe(responsePacketType, onResponseReceived);
        }
    }
}
