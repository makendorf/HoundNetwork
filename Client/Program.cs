using HoundNetwork;
using HoundNetwork.NetworkModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
