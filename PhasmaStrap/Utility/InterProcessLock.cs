using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhasmaStrap.Utility
{
    public class InterProcessLock : IDisposable
    {
        public Mutex Mutex { get; private set; }

        public bool IsAcquired { get; private set; }

        public InterProcessLock(string name) : this(name, TimeSpan.Zero) { }

        private readonly int _ownerThread;

        public InterProcessLock(string name, TimeSpan timeout)
        {
            Mutex = new Mutex(false, "PhasmaStrap-" + name);

            try
            {
                IsAcquired = Mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                IsAcquired = true;
            }

            _ownerThread = Environment.CurrentManagedThreadId;
        }

        public void Dispose()
        {
            if (IsAcquired)
            {
                if (_ownerThread == Environment.CurrentManagedThreadId)
                {
                    try
                    {
                        Mutex.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                    }
                }

                IsAcquired = false;
            }

            Mutex.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
