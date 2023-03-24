using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HoundNetwork.NetworkModels
{
    [Serializable]
    public enum TypePacket
    {
        None = 0,
        Message = 1,
        Registration = 2,
        KeepAlive = 3,
        ClientDisconnect = 4,
        Document = 5,
        PacketCallback = 6
    }
    [Serializable]
    public struct Callback
    {
        public Guid Guid { get; set; }
        public int FragmentPacket { get; set; }
    }
}
