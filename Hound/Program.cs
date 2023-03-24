using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HoundNetwork;
using HoundNetwork.NetworkModels;

namespace Hound
{
    internal class Program
    {
        private static HoundServer server = new HoundServer("127.0.0.1", 5000);
        private static HoundClient client = new HoundClient("127.0.0.1", 5000, Guid.NewGuid(), "Тестовый клиент");
        public static async Task<string> qwe()
        {
            await Task.Delay(3000);
            return "Хуесос";
        }

        public static async Task Main()
        {
            _ = Task.Run(() => server.StartAsync());

            await client.ConnectAsync();

            Guid clientId = Guid.NewGuid();
            NetworkPayload registrationPayload = new NetworkPayload(clientId, Guid.Empty, TypePacket.Registration, client.Client.DisplayName);
            await client.SendDataAsync(registrationPayload);
            await Task.Delay(2000);


            //string message = "Здарова ебать!";
            //NetworkPayload messagePayload = new NetworkPayload(clientId, Guid.Empty, TypePacket.Message, message);
            //await client.SendDataAsync(messagePayload);
            //await Task.Delay(2000);

            //var documentPayload = new NetworkPayload()
            //{
            //    ObjectData = File.ReadAllBytes(@"D:\example.xlsm"),
            //    PacketType = TypePacket.Document
            //};
            //await client.SendDataAsync(documentPayload);

            //Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
