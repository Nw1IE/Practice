using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Projects.StationLogger
{
    public class StationLogger
    {
        private readonly object _lockObj = new();

        public void Log(int cellId, string status, string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var threadId = Environment.CurrentManagedThreadId;

            lock (_lockObj)
            {
                Console.WriteLine($"[{timestamp}] [Thr-{threadId,2}] [Cell-{cellId,2}] {status,-22} | {message}");
            }
        }

        public void LogSystem(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var threadId = Environment.CurrentManagedThreadId;

            lock (_lockObj)
            {
                Console.WriteLine($"[{timestamp}] [Thr-{threadId,2}] [ SYSTEM ] {message}");
            }
        }
    }
}
