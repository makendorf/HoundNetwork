using HoundNetwork;
using HoundNetwork.NetworkModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    internal class Program
    {
        public static HoundServer server = new HoundServer("Server");
        static void Main(string[] args)
        {
            _ = Task.Run(() => server.StartAsync());
            Task.WaitAny(Task.Delay(5000));

            var payload = new NetworkPayload
            {
                PacketType = TypePacket.ClientDisconnect
            };
            _ = server.SendDataAsync(server._connectedClients.ElementAt(0).Value, payload);

            //var payload = new NetworkPayload
            //{
            //    ObjectData = File.ReadAllBytes(@"D:\Download\oboi_prirody180715.zip"),
            //    PacketType = TypePacket.Document
            //};
            //_ = server.SendDataAsync(server._connectedClients.ElementAt(0).Value, payload);
            Console.ReadKey();
        }
    }
}
