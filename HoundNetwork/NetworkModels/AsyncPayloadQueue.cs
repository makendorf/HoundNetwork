using HoundNetwork.NetworkModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HoundNetwork.NetworkModels
{
    public class AsyncPayloadQueue
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _notEmpty = new SemaphoreSlim(0);
        private readonly List<Subscription> _subscriptions = new List<Subscription>();
        private readonly ReaderWriterLockSlim _subscriptionLock = new ReaderWriterLockSlim();

        public async Task EnqueueAsync(NetworkPayload payload)
        {
            await _semaphore.WaitAsync();
            try
            {
                _notEmpty.Release();
                _subscriptionLock.EnterReadLock();
                try
                {
                    foreach (var subscription in _subscriptions)
                    {
                        if (subscription.PacketType == payload.PacketType)
                        {
                            subscription.Notify(payload);
                        }
                    }
                }
                finally
                {
                    _subscriptionLock.ExitReadLock();
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public Subscription Subscribe(TypePacket packetType, Action<NetworkPayload> callback)
        {
            var subscription = new Subscription(packetType, callback, _subscriptions, _subscriptionLock);
            _subscriptionLock.EnterWriteLock();
            try
            {
                _subscriptions.Add(subscription);
            }
            finally
            {
                _subscriptionLock.ExitWriteLock();
            }
            return subscription;
        }
    }

    public class Subscription : IDisposable
    {
        public TypePacket PacketType { get; }
        private readonly Action<NetworkPayload> _callback;
        private readonly List<Subscription> _subscriptions;
        private readonly ReaderWriterLockSlim _subscriptionLock;

        public Subscription(TypePacket packetType, Action<NetworkPayload> callback, List<Subscription> subscriptions, ReaderWriterLockSlim subscriptionLock)
        {
            PacketType = packetType;
            _callback = callback;
            _subscriptions = subscriptions;
            _subscriptionLock = subscriptionLock;
        }

        public void Notify(NetworkPayload payload)
        {
            _callback(payload);
        }

        public void Dispose()
        {
            _subscriptionLock.EnterWriteLock();
            try
            {
                _subscriptions.Remove(this);
            }
            finally
            {
                _subscriptionLock.ExitWriteLock();
            }
        }
    }


}
