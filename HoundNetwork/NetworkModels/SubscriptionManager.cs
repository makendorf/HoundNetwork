using System;
using System.Collections.Generic;
using System.Threading;

namespace HoundNetwork.NetworkModels
{
    public class SubscriptionManager
    {
        private readonly Dictionary<Guid, Subscription<object>> _subscriptions = new Dictionary<Guid, Subscription<object>>();
        private readonly ReaderWriterLockSlim _subscriptionLock = new ReaderWriterLockSlim();

        public void Enqueue(int PacketType, IncomingData Object)
        {
            try
            {
                _subscriptionLock.EnterWriteLock();
                try
                {
                    if (_subscriptions.ContainsKey(Object.Guid))
                    {
                        _subscriptions[Object.Guid].Notify(Object);
                    }
                    else
                    {
                        foreach (var subscription in _subscriptions)
                        {
                            if (subscription.Value.PacketType == PacketType)
                            {
                                subscription.Value.Notify(Object);
                            }
                        }
                    }
                }
                finally
                {
                    _subscriptionLock.ExitWriteLock();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        public Subscription<object> Subscribe(Guid guid, int packetType, Action<object> callback)
        {
            var subscription = new Subscription<object>(guid, packetType, callback, _subscriptions, _subscriptionLock);
            _subscriptionLock.EnterWriteLock();
            try
            {
                _subscriptions.Add(guid, subscription);
            }
            finally
            {
                _subscriptionLock.ExitWriteLock();
            }
            return subscription;
        }
        public Subscription<object> Subscribe(int packetType, Action<object> callback)
        {
            Guid guid = Guid.NewGuid();
            var subscription = new Subscription<object>(guid, packetType, callback, _subscriptions, _subscriptionLock);
            _subscriptionLock.EnterWriteLock();
            try
            {
                _subscriptions.Add(guid, subscription);
            }
            finally
            {
                _subscriptionLock.ExitWriteLock();
            }
            return subscription;
        }
        public void Flush()
        {
            _subscriptions.Clear();
        }
    }

    public class Subscription<T> : IDisposable
    {
        public Guid Guid { get; private set; }
        public int PacketType { get; }
        private readonly Action<T> _callback;
        private readonly Dictionary<Guid, Subscription<T>> _subscriptions;
        private readonly ReaderWriterLockSlim _subscriptionLock;

        public Subscription(Guid guid, int packetType, Action<T> callback, Dictionary<Guid, Subscription<T>> subscriptions, ReaderWriterLockSlim subscriptionLock)
        {
            PacketType = packetType;
            _callback = callback;
            _subscriptions = subscriptions;
            _subscriptionLock = subscriptionLock;
            Guid = guid;
        }

        public void Notify(T Object)
        {
            _callback(Object);
        }

        public void Dispose()
        {
            _subscriptionLock.EnterWriteLock();
            try
            {
                _subscriptions.Remove(Guid);
            }
            finally
            {
                _subscriptionLock.ExitWriteLock();
            }
        }
    }
}
