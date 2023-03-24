using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HoundNetwork.NetworkModels
{
    public class ClientInfo
    {
        public Guid Guid { get; set; }
        public string DisplayName { get; set; }
        public TcpClient Client { get; set; }
        public bool IsConnected => Client?.Client?.Connected ?? false;
        public bool RegistrationFromServer { get; private set; }
        public CancellationTokenSource CancellationTokenSource { get; private set; } = new CancellationTokenSource();
        public AsyncPayloadQueue MessageQueue { get; } = new AsyncPayloadQueue();
        public DateTime LastActivityTime { get; private set; } = DateTime.Now;

        public ClientInfo(Guid guid, string displayName, TcpClient client)
        {
            Guid = guid;
            DisplayName = displayName;
            Client = client;
        }
        public ClientInfo()
        {

        }
        public void UpdateCancellationTokenSource()
        {
            CancellationTokenSource = new CancellationTokenSource();
        }
        public CancellationTokenSource GetCancellationTokenSource()
        {
            return CancellationTokenSource;
        }
        public void SetRegistration()
        {

            RegistrationFromServer = !RegistrationFromServer;
        }
        public bool GetRegistration()
        {
            return RegistrationFromServer;
        }
        public void UpdateLastActivityTime()
        {
            LastActivityTime = DateTime.Now;
        }
        public DateTime GetLastActivityTime()
        {
            return LastActivityTime;
        }
    }
}
