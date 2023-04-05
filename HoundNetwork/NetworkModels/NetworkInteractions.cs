using HoundNetwork.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace HoundNetwork.NetworkModels
{
    
    
    public class NetworkInteractions
    {
        private string _displayname;
        private bool _registration;
        private Guid guid = Credentions.Default.ServerGuid;
        public int Port { get { return Credentions.Default.ServerPort; } }
        public string ServerAddress { get { return Credentions.Default.ServerIP; } }
        public string DisplayName { get { return _displayname; } set { _displayname = value; } }
        public Guid GUID { get { return guid; } set { guid = value; } }
        private bool Registration { get { return _registration; } set { _registration = value; } }
        private CancellationTokenSource CancellationTokenSource { get; set; } = new CancellationTokenSource();
        public SubscriptionManager subscriptionManager { get; } = new SubscriptionManager();

        public async Task ReceiveDataAsync(HoundClient Client)
        {
            int bufferLength = 64 * 1024;
            byte[] streamBuffer = new byte[bufferLength];
            Dictionary<Guid, List<NetworkPacket>> receivedFragments = new Dictionary<Guid, List<NetworkPacket>>();

            while (Client.Client.Connected)
            {
                try
                {
                    GetCancellationTokenSource().Token.ThrowIfCancellationRequested();
                    NetworkStream stream = Client.Client.GetStream();
                    (bool, string) resultDeserialize = (true, "");

                    MemoryStream ms = new MemoryStream();
                    while (true)
                    {
                        int bytesRead = await stream.ReadAsync(streamBuffer, 0, streamBuffer.Length, GetCancellationTokenSource().Token);

                        if (bytesRead > 0)
                        {
                            ms.Write(streamBuffer, 0, bytesRead);

                            using (MemoryStream msRead = new MemoryStream(ms.ToArray()))
                            {
                                using (BinaryReader br = new BinaryReader(msRead))
                                {
                                    br.BaseStream.Position = 0;

                                    while (msRead.Position < msRead.Length)
                                    {
                                        try
                                        {
                                            var packetLenght = br.ReadInt32();
                                            var packetEnding = br.ReadByte() == 0;

                                            if (packetLenght > 0 && msRead.Length >= msRead.Position + packetLenght)
                                            {
                                                byte[] data = br.ReadBytes(packetLenght);
                                                resultDeserialize = NetworkSerialization.DeserializeNetworkPacket(data, out NetworkPacket payload);
                                                if (resultDeserialize.Item1)
                                                {
                                                    CombineFragmentFromDictonary(Client, receivedFragments, payload);
                                                }
                                            }
                                            else
                                            {
                                                break;
                                            }
                                        }
                                        catch (EndOfStreamException)
                                        {
                                            break;
                                        }
                                    }
                                }
                            }

                            long unreadBytes = ms.Length - ms.Position;
                            if (unreadBytes > 0)
                            {
                                byte[] unreadData = new byte[unreadBytes];
                                ms.Read(unreadData, 0, (int)unreadBytes);
                                ms.SetLength(0);
                                ms.Write(unreadData, 0, (int)unreadBytes);
                            }
                            else
                            {
                                ms.SetLength(0);
                            }
                        }
                    }

                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Операция была отменена.");
                }
                catch (IOException)
                {
                    throw new IOException("Disconect");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"{DisplayName}: {e.InnerException}:\n{e.Message}\n{e.StackTrace}");
                }
            }
        }
        private void CombineFragmentFromDictonary(HoundClient Client, Dictionary<Guid, List<NetworkPacket>> receivedFragments, NetworkPacket payload)
        {
            try
            {
                if (payload.TotalFragments > 1)
                {
                    if (!receivedFragments.ContainsKey(payload.PacketId))
                    {
                        receivedFragments[payload.PacketId] = new List<NetworkPacket>();
                    }

                    receivedFragments[payload.PacketId].Add(payload);
                    if (payload.FragmentIndex == 0)
                    {
                        Console.WriteLine($"Прием пакета {payload.FragmentIndex + 1} из {payload.TotalFragments}");
                    }
                    else
                    {
                        Console.WriteLine($"\rПрием пакета {payload.FragmentIndex + 1}[{receivedFragments[payload.PacketId].Count}] из {payload.TotalFragments}");
                    }

                    if (receivedFragments[payload.PacketId].Count == payload.TotalFragments)
                    {
                        var combinedPayload = NetworkSerialization.CombineFragments(receivedFragments[payload.PacketId]);

                        Client.subscriptionManager.Enqueue(combinedPayload.PacketType, new IncomingData() 
                        { 
                            Client = Client, 
                            Payload = combinedPayload, 
                            Guid = receivedFragments[payload.PacketId][0].PacketId 
                        });

                        receivedFragments.Remove(payload.PacketId);
                    }
                }
                else
                {
                    NetworkSerialization.Deserialize(payload.Data, out object onePayload);
                    Client.subscriptionManager.Enqueue(((NetworkPayload)onePayload).PacketType, new IncomingData() 
                    { 
                        Client = Client, 
                        Payload = (NetworkPayload)onePayload, 
                        Guid = payload.PacketId 
                    });
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
        public async Task<Guid> SendDataAsync(HoundClient client, NetworkPayload payload) => await SendDataAsync(client, payload, Guid.NewGuid());
        public async Task<Guid> SendDataAsync(HoundClient client, NetworkPayload payload, Guid guid)
        {
            if (client.Client.Connected)
            {
                Guid packetId = guid;
                List<byte[]> serializedData = NetworkSerialization.PreparingForSend(packetId, payload).ToList();
                
                if (serializedData.Count > 1)
                {
                    for (int i = 0; i < serializedData.Count; i++)
                    {
                        try
                        {
                            byte[] byteItem = new byte[serializedData[i].Length + 5];
                            BitConverter.GetBytes(serializedData[i].Length).CopyTo(byteItem, 0);
                            (i < serializedData.Count - 1 ? new byte[1] { 1 } : new byte[1] { 0 }).CopyTo(byteItem, 4);
                            serializedData[i].CopyTo(byteItem, 5);

                            NetworkStream stream = client.Client.GetStream();
                            await stream.WriteAsync(byteItem, 0, byteItem.Length);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"{DisplayName}: Ошибка отправки данных: {e.Message}");
                        }
                        if(i % 4 == 0)
                        {
                            await Task.Delay(1);
                        }
                        
                    }
                }
                else
                {
                    byte[] byteItem = new byte[serializedData[0].Length + 5];
                    BitConverter.GetBytes(serializedData[0].Length).CopyTo(byteItem, 0);
                    (new byte[1] { 0 }).CopyTo(byteItem, 4);
                    serializedData[0].CopyTo(byteItem, 5);

                    NetworkStream stream = client.Client.GetStream();
                    await stream.WriteAsync(byteItem, 0, byteItem.Length);
                }
                return packetId;
            }
            return Guid.Empty;
        }
        public SubscriptionManager GetPacketQueue()
        {
            return subscriptionManager;
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


        public void Subscribe(HoundClient client, int responsePacketType, Action<object> onResponseReceived)
        {
            client.subscriptionManager.Subscribe(responsePacketType, onResponseReceived);
        }
        public void Subscribe(Guid guid, HoundClient client, int responsePacketType, Action<object> onResponseReceived)
        {
            client.subscriptionManager.Subscribe(guid, responsePacketType, onResponseReceived);
        }
        public async Task<IncomingData> SendAndWaitResponseAsync(HoundClient client, NetworkPayload payload, int typePacket = 0)
        {
            var checkReceived = new TaskCompletionSource<bool>();
            IncomingData payloadReceived = new IncomingData();
            Guid packetID = Guid.NewGuid();
            if (packetID != Guid.Empty)
            {
                using (client.subscriptionManager.Subscribe(packetID, typePacket == 0 ? payload.PacketType : typePacket, (obj) =>
                {
                    payloadReceived = (IncomingData)obj;
                    Task.Run(() => checkReceived.SetResult(true));
                }))
                {
                    await SendDataAsync(client, payload);
                    await checkReceived.Task;
                }
            }
            
            return payloadReceived;
        }
    }
}
