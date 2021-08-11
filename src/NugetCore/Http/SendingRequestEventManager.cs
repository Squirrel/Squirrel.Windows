using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace NuGet
{
    // Implement weak event pattern. Read more here:
    // http://msdn.microsoft.com/en-us/library/aa970850(v=vs.100).aspx
    public class SendingRequestEventManager : WeakEventManager
    {
        private static readonly object _managerLock = new object();

        public static void AddListener(IHttpClientEvents source, IWeakEventListener listener)
        {
            // weak event pattern cannot be used if we're running from command line.
            Debug.Assert(!EnvironmentUtility.RunningFromCommandLine);

            SendingRequestEventManager.CurrentManager.ProtectedAddListener(source, listener);
        }

        public static void RemoveListener(IHttpClientEvents source, IWeakEventListener listener)
        {
            // weak event pattern cannot be used if we're running from command line.
            Debug.Assert(!EnvironmentUtility.RunningFromCommandLine);

            SendingRequestEventManager.CurrentManager.ProtectedRemoveListener(source, listener);
        }

        private static SendingRequestEventManager CurrentManager
        {
            get
            {
                Type managerType = typeof(SendingRequestEventManager);

                lock (_managerLock)
                {
                    SendingRequestEventManager manager = (SendingRequestEventManager)WeakEventManager.GetCurrentManager(managerType);
                    if (manager == null)
                    {
                        manager = new SendingRequestEventManager();
                        WeakEventManager.SetCurrentManager(managerType, manager);
                    }

                    return manager;
                }
            }
        } 

        protected override void StartListening(object source)
        {
            var clientEvents = (IHttpClientEvents)source;
            clientEvents.SendingRequest += OnSendingRequest;
        }

        protected override void StopListening(object source)
        {
            var clientEvents = (IHttpClientEvents)source;
            clientEvents.SendingRequest -= OnSendingRequest;
        }

        private void OnSendingRequest(object sender, WebRequestEventArgs e)
        {
            base.DeliverEvent(sender, e);
        }
    }
}
