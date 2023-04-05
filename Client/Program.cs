using HoundNetwork;
using System;
using System.Threading.Tasks;

namespace Client
{
    internal class Program
    {
        public static HoundClient client = new HoundClient("TestClient");

        static void Main(string[] args)
        {
            _ = Task.Run(async () => await client.ConnectAsync());
            
            Console.ReadKey();
        }
    }
}
