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
        private static ServerInfo server = new ServerInfo("Сервер");
        private static ClientInfo client = new ClientInfo("Тестовый клиент");
        public static async Task<string> qwe()
        {
            await Task.Delay(3000);
            return "Хуесос";
        }
        public static async Task Main()
        {
            _ = Task.Run(() => server.StartAsync());

            await client.ConnectAsync();
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
