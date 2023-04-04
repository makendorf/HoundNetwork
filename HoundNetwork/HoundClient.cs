using HoundNetwork.NetworkModels;
using HoundNetwork.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace HoundNetwork
{
    public class HoundClient : NetworkInteractions
    {
        private TcpClient _client;

        public TcpClient Client { get => _client; set { _client = value; } }
        public Guid ServerGuid = Credentions.Default.ServerGuid;
        public HoundClient(string displayname)
        {
            Client = new TcpClient();
            DisplayName = displayname;
            NetworkSerialization.Sender = GUID;
            NetworkSerialization.Receiver = ServerGuid;
        }
        public HoundClient()
        {
            NetworkSerialization.Sender = GUID;
            NetworkSerialization.Receiver = ServerGuid;
        }
        public async Task ConnectAsync(int timeoutMs = 5000)
        {
            try
            {
                Console.WriteLine("Соединение с " + ServerAddress);
                var connectTask = Client.ConnectAsync(ServerAddress, Port);
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask)
                {
                    throw new TimeoutException("Тайм-аут соединения.");
                }
                
                await connectTask;

                RunInitialMethods();

            }
            catch(SocketException e)
            {
                Console.WriteLine($"{DisplayName}: {e.Message}");
                await ReconnectAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private void RunInitialMethods()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ReceiveDataAsync(this);
                }
                catch (IOException)
                {
                    GetCancellationTokenSource().Cancel();
                    await ReconnectAsync();
                }
            });

            SendRegistrationRequest();

            _ = Task.Run(async () => await SendKeepAliveAsync(TimeSpan.FromSeconds(5)));

            subscriptionManager.Subscribe(TypePacket.ClientDisconnect, (Obj) =>
            {
                DisconectSubscribe();
            });
        }

        private async Task ReconnectAsync(int timeout = 60000)
        {
            int retryDelayMs = 1000;
            int curDelayMs = 0;

            GetCancellationTokenSource().Cancel();
            while (GetCancellationTokenSource().Token.IsCancellationRequested)
            {
                try
                {
                    curDelayMs += retryDelayMs;
                    Client = new TcpClient();
                    subscriptionManager.Flush();
                    await Client.ConnectAsync(ServerAddress, Port);
                    UpdateCancellationTokenSource();
                    RunInitialMethods();
                }
                catch
                {
                    if (curDelayMs > timeout)
                    {
                        Console.WriteLine($"Тайм-аут переподключения. ({timeout} мс.)");
                        break;
                    }
                    Console.WriteLine($"Попытка переподключения №{(curDelayMs / 1000)}");
                    await Task.Delay(retryDelayMs);
                    
                    
                }
            }
        }
        private async Task SendKeepAliveAsync(TimeSpan interval)
        {
            if (GetRegistration())
            {
                while (!GetCancellationTokenSource().IsCancellationRequested)
                {
                    await Task.Delay(interval);
                    NetworkPayload keepAlivePayload = new NetworkPayload
                    {
                        PacketType = TypePacket.KeepAlive,
                        ObjectData = Array.Empty<byte>()
                    };

                    await SendDataAsync(this, keepAlivePayload);
                }
            }
            else
            {
                await Task.Delay(interval);
                await SendKeepAliveAsync(interval);
            }
        }

        private async void SendRegistrationRequest()
        {
            NetworkPayload registrationPayload = new NetworkPayload(TypePacket.Registration, DisplayName);
            var response = await SendAndWaitResponseAsync(this, registrationPayload);

            var guid = (Guid)response.Payload.ObjectData;
            Console.WriteLine($"Смена GUID: {GUID} -> {guid}");
            SetRegistration(true);
            GUID = guid;
        }
        private void DisconectSubscribe()
        {
            Console.WriteLine("Завершение работы");
            GetCancellationTokenSource().Cancel();
            Environment.Exit(0);
        }
    }
}
