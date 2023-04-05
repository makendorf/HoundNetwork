using HoundNetwork.NetworkModels;
using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net;
using System.Threading.Tasks;
using System.IO;

namespace HoundNetwork
{
    public class HoundServer : NetworkInteractions
    {
        private TcpListener _tcpListener;
        public ConcurrentDictionary<Guid, HoundClient> _connectedClients { get; private set; } = new ConcurrentDictionary<Guid, HoundClient>();
        public TcpListener TcpListener { get => _tcpListener; private set { _tcpListener = value; } }
        public HoundServer(string displayname)
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
                var _client = new HoundClient
                {
                    Client = client
                };

                var newClientGuid = Guid.NewGuid();
                _client.GUID = newClientGuid;

                _connectedClients.TryAdd(newClientGuid, _client);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ReceiveDataAsync(_client);
                    }
                    catch (IOException)
                    {
                        DisconectClient(_client);
                    }
                });

                Subscribe(_client, (int)TypePacket.Registration, (obj) => ClientSendRegistrationRequest((IncomingData)obj));

                Subscribe(_client, (int)TypePacket.KeepAlive, (obj) => KeepAliveRequested());

                
            }
        }
        private void DisconectClient(HoundClient client)
        {
            _connectedClients[client.GUID].GetCancellationTokenSource().Cancel();
            _connectedClients.TryRemove(client.GUID, out HoundClient _client);
            Console.WriteLine($"{_client.DisplayName} отключен от сервера.");
        }
        private void KeepAliveRequested()
        {
            
        }
        private async void ClientSendRegistrationRequest(IncomingData data)
        {
            var payload = data.Payload;
            var client = data.Client;
            string displayName = (string)payload.ObjectData;

            Console.WriteLine($"Регистрация клиента: {displayName} : {client.GUID}");
            _connectedClients.TryGetValue(client.GUID, out HoundClient clientInfo);
            clientInfo.DisplayName = displayName;
            clientInfo.SetRegistration(true);
            var response = new NetworkPayload
            {
                ObjectData = client.GUID,
                PacketType = (int)TypePacket.Registration,
            };
            await SendDataAsync(client, response, data.Guid);
        }
    }
}
