using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HoundNetwork.NetworkModels
{
    [Serializable]
    public class NetworkPayload
    {
        public TypePacket PacketType { get; set; }
        
        public object ObjectData { get; set; }

        public NetworkPayload(TypePacket packetType, object obj)
        {
            PacketType = packetType;
            ObjectData = obj ?? null;
        }
        public NetworkPayload()
        {
            PacketType = TypePacket.None;
            ObjectData = ObjectData ?? null;
        }
    }
    [Serializable]
    public struct NetworkPacket
    {
        public Guid PacketId { get; set; }
        public Guid Sender { get; set; }
        public Guid Receiver { get; set; }
        public int FragmentIndex { get; set; }
        public int TotalFragments { get; set; }
        public byte[] Data { get; set; }
    }
    
    public static class NetworkSerialization
    {
        public static Guid Sender = Guid.Empty;
        public static Guid Receiver = Guid.Empty;
        public static bool Serialize(object obj, out byte[] bytes)
        {
            byte[] _bytes;
            BinaryFormatter formatter = new BinaryFormatter();
            using (MemoryStream stream = new MemoryStream())
            {
                formatter.Serialize(stream, obj);
                _bytes = stream.ToArray();
            }
            bytes = _bytes;
            return true;
        }
        private static IEnumerable<NetworkPacket> DeployData(Guid packetId, NetworkPayload payload)
        {
            Serialize(payload, out byte[] payloadData);
            int maxFragmentSize = 4096;
            int totalFragments = (int)Math.Ceiling((double)payloadData.Length / maxFragmentSize);

            for (int i = 0; i < totalFragments; i++)
            {
                int fragmentSize = (i == totalFragments - 1) ? payloadData.Length - i * maxFragmentSize : maxFragmentSize;
                byte[] fragmentData = new byte[fragmentSize];
                Array.Copy(payloadData, i * maxFragmentSize, fragmentData, 0, fragmentSize);
                yield return new NetworkPacket
                {
                    PacketId = packetId,
                    Sender = Sender,
                    Receiver = Receiver,
                    TotalFragments = totalFragments,
                    FragmentIndex = i,
                    Data = fragmentData
                };
            }
        }
        public static IEnumerable<byte[]> PreparingForSend(Guid guid, NetworkPayload payload)
        {
            foreach (var byteItem in DeployData(guid, payload))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    using (BinaryWriter bw = new BinaryWriter(ms))
                    {
                        bw.Write(byteItem.PacketId.ToByteArray());
                        bw.Write(byteItem.Sender.ToByteArray());
                        bw.Write(byteItem.Receiver.ToByteArray());

                        bw.Write(byteItem.TotalFragments);
                        bw.Write(byteItem.FragmentIndex);

                        bw.Write(byteItem.Data.Length);
                        bw.Write(byteItem.Data);
                    }
                    yield return ms.ToArray();
                }
            }
        }
        public static (bool, string) DeserializeNetworkPacket(byte[] data, out NetworkPacket result)
        {
            using (var ms = new MemoryStream(data))
            {
                using (var br = new BinaryReader(ms))
                {
                    try
                    {
                        Guid packetId = new Guid(br.ReadBytes(16));
                        Guid packetSender = new Guid(br.ReadBytes(16));
                        Guid packetReceiver = new Guid(br.ReadBytes(16));

                        int totalFragments = br.ReadInt32();
                        int fragmentIndex = br.ReadInt32();

                        int dataSize = br.ReadInt32();
                        byte[] payloadData = br.ReadBytes(dataSize);

                        result = new NetworkPacket
                        {
                            PacketId = packetId,
                            Sender = packetSender,
                            Receiver = packetReceiver,
                            TotalFragments = totalFragments,
                            FragmentIndex = fragmentIndex,
                            Data = payloadData
                        };
                        return (true, "Успешно");
                    }
                    catch (Exception exc)
                    {
                        result = new NetworkPacket();
                        return (false, $"Ошибка: {exc.Message}\nТрассировка исключения: {exc.StackTrace}");
                    }
                }
            }
        }
        public static NetworkPayload CombineFragments(List<NetworkPacket> fragments)
        {
            if (fragments == null || fragments.Count == 0)
            {
                throw new ArgumentException("Список пакетов пустой.");
            }

            fragments.Sort((a, b) => a.FragmentIndex.CompareTo(b.FragmentIndex));
            byte[] combinedData = new byte[fragments.Sum(f => f.Data.Length)];

            int currentIndex = 0;
            foreach (var fragment in fragments)
            {
                Array.Copy(fragment.Data, 0, combinedData, currentIndex, fragment.Data.Length);
                currentIndex += fragment.Data.Length;
            }
            Deserialize(combinedData, out object combinedPayload);
            return (NetworkPayload)combinedPayload;
        }
        public static (bool,string) Deserialize(byte[] Data, out object obj)
        {
            try
            {
                using (var memStream = new MemoryStream())
                {
                    var binForm = new BinaryFormatter();
                    memStream.Write(Data, 0, Data.Length);
                    memStream.Seek(0, SeekOrigin.Begin);
                    obj = binForm.Deserialize(memStream);
                }

                return (true, "Успешно");
            }
            catch (Exception exc)
            {
                obj = null;
                return (false, $"Ошибка: {exc.Message}\nТрассировка исключения: {exc.StackTrace}");
            }
        }
    }
}
