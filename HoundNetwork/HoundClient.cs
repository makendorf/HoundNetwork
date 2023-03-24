using HoundNetwork.NetworkModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace HoundNetwork
{
    public class HoundClient
    {
        private string _serverAddress;
        private int _port;
        private ClientInfo _client = new ClientInfo();

        public ClientInfo Client { get => _client; private set { _client = value; } }

        public HoundClient(string serverAddress, int port, Guid guid, string name)
        {
            _serverAddress = serverAddress;
            _port = port;
            Client.Guid = guid;
            Client.DisplayName = name;
        }

        public async Task ConnectAsync(int timeoutMs = 5000)
        {
            try
            {
                Client.Client = new TcpClient();

                var connectTask = Client.Client.ConnectAsync(IPAddress.Parse(_serverAddress), _port);
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask)
                {
                    throw new TimeoutException("Тайм-аут соединения.");
                }

                await connectTask;

                _ = Task.Run(async () => await ReceiveDataAsync());

                _ = Task.Run(async () => await PeriodicConnectionCheckAsync(TimeSpan.FromMinutes(5)));

                _ = Task.Run(async () => await SendKeepAliveAsync(TimeSpan.FromSeconds(5)));

                _ = Task.Run(async () => await RegistrationSubscribe());

                _ = Task.Run(async () => await DisconectSubscribe());

                //_ = Task.Run(async () => await CheckCancelationToken());
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private Task CheckCancelationToken()
        {
            while (true)
            {
                if (Client.CancellationTokenSource.IsCancellationRequested)
                {
                    Console.Out.WriteLineAsync("Хуй");
                }
            }
        }

        private async Task ReceiveDataAsync()
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
                                            if (payload.FragmentIndex == payres)
                                            {
                                                await CombineFragmentFromDictonary(receivedFragments, payload);
                                                await SendPacketCallback(payload.PacketId, payload.FragmentIndex);
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
                    await ReconnectAsync();
                    
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Клиент: ошибка обработки данных:\n{e.Message}\n{e.StackTrace}");
                }
            }
        }
        public async Task SendDataAsync(NetworkPayload payload)
        {
            if (Client.Client.Connected)
            {
                var serializedData = NetworkSerialization.PreparingForSend(payload).ToList();
                var guidBytes = new byte[16];
                Array.Copy(serializedData[0], guidBytes, 16);
                var packetID = new Guid(guidBytes);
                for (int i = 0; i < serializedData.Count; i++)
                {
                    try
                    {
                        NetworkStream stream = Client.Client.GetStream();
                        byte[] byteItem = serializedData[i];
                        await stream.WriteAsync(byteItem, 0, byteItem.Length);
                        using (var messageSubscription = Client.MessageQueue.Subscribe(TypePacket.PacketCallback, packet =>
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
        private async Task CombineFragmentFromDictonary(Dictionary<Guid, List<NetworkPacket>> receivedFragments, NetworkPacket payload)
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

                    await Client.MessageQueue.EnqueueAsync(combinedPayload);

                    receivedFragments.Remove(payload.PacketId);
                }
            }
            else
            {
                NetworkSerialization.Deserialize(payload.Data, out object onePayload);
                await Client.MessageQueue.EnqueueAsync((NetworkPayload)onePayload);
            }
        }
        private async Task SendPacketCallback(Guid packetId, int fragmentIndex)
        {
            byte[] data = new byte[20];
            using (var ms = new MemoryStream(data))
            {
                using (var br = new BinaryWriter(ms))
                {
                    br.Write(packetId.ToByteArray());
                    br.Write(fragmentIndex);
                    NetworkPayload payload = new NetworkPayload(Guid.Empty, Client.Guid, TypePacket.PacketCallback, ms.ToArray());
                    await SendDataAsync(payload);
                }
            }

        }
        private async Task RegistrationSubscribe()
        {
            using (var registrationSubscribe = Client.MessageQueue.Subscribe(TypePacket.Registration, payload =>
            {
                var guid = (Guid)payload.ObjectData;
                Console.WriteLine($"Смена GUID: {Client.Guid} -> {guid}");
                _client.SetRegistration();
                Client.Guid = guid;

            }))
            {
                await Task.Delay(100);
            }
        }
        private async Task DisconectSubscribe()
        {
            using (var disconectSubscribe = Client.MessageQueue.Subscribe(TypePacket.ClientDisconnect, payload =>
            {
                Console.WriteLine("Завершение работы");
                _client.GetCancellationTokenSource().Cancel();
            }))
            {
                await Task.Delay(100);
            }
        }
       
        private async Task SendKeepAliveAsync(TimeSpan interval)
        {
            if (_client.GetRegistration())
            {
                while (!_client.GetCancellationTokenSource().IsCancellationRequested)
                {
                    NetworkPayload keepAlivePayload = new NetworkPayload
                    {
                        PacketType = TypePacket.KeepAlive,
                        Sender = Client.Guid,
                        ObjectData = Array.Empty<byte>()
                    };

                    await SendDataAsync(keepAlivePayload);

                    await Task.Delay(interval);
                }
            }
        }
        private async Task ReconnectAsync()
        {
            int retryDelayMs = 1000;
            int maxRetryDelayMs = 60000;

            while (!Client.Client.Connected || Client.GetCancellationTokenSource().Token.IsCancellationRequested)
            {
                try
                {
                    await Client.Client.ConnectAsync(IPAddress.Parse(_serverAddress), _port);
                }
                catch 
                {
                    await Task.Delay(retryDelayMs);
                    retryDelayMs = Math.Min(retryDelayMs * 2, maxRetryDelayMs);
                }
            }
        }
        private async Task PeriodicConnectionCheckAsync(TimeSpan interval)
        {
            while (!_client.GetCancellationTokenSource().IsCancellationRequested)
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
    }
}
