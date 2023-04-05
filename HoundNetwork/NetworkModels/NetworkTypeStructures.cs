using System;

namespace HoundNetwork.NetworkModels
{
    [Serializable]
    public enum TypePacket
    {
        None = 0,
        Registration = 1,
        KeepAlive = 2,
        ClientDisconnect = 4,
    }
    public struct IncomingData
    {
        public HoundClient Client;
        public NetworkPayload Payload;
        public Guid Guid;
    }
}
