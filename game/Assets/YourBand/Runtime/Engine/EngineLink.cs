using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using YourBand.Core.Engine;
using Newtonsoft.Json.Linq;

namespace YourBand.Runtime.Engine
{
    /// <summary>
    /// The connection to the audio engine (schema/engine-protocol.md): connects to it on this computer and keeps
    /// trying while it is not running. Messages from the engine are handed over on the main thread, in
    /// <see cref="Poll"/>.
    /// </summary>
    public sealed class EngineLink : IDisposable
    {
        readonly ConcurrentQueue<(string type, JObject message)> incoming = new ConcurrentQueue<(string, JObject)>();
        readonly object sending = new object();
        readonly Thread thread;
        volatile bool stopping;
        volatile bool connected;
        bool reported;
        TcpClient client;
        NetworkStream stream;

        /// <summary>A message from the engine (main thread).</summary>
        public event Action<string, JObject> Message;
        /// <summary>The engine connected or went (main thread).</summary>
        public event Action<bool> ConnectionChanged;

        public bool Connected => connected;

        public EngineLink()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "Engine link" };
        }

        public void Start() => thread.Start();

        public void Send(string line)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            lock (sending)
            {
                try
                {
                    stream?.Write(bytes, 0, bytes.Length);
                }
                catch (IOException)
                {
                    // the reader notices the connection is gone
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        /// <summary>Hands over what came in (call from the main thread, every frame).</summary>
        public void Poll()
        {
            if (connected != reported)
            {
                reported = connected;
                ConnectionChanged?.Invoke(reported);
            }
            while (incoming.TryDequeue(out var m))
                Message?.Invoke(m.type, m.message);
        }

        void Run()
        {
            while (!stopping)
            {
                try
                {
                    var c = new TcpClient { NoDelay = true };
                    if (!c.ConnectAsync("127.0.0.1", EngineProtocol.Port).Wait(500) || !c.Connected)
                    {
                        c.Close();
                        Thread.Sleep(1500);
                        continue;
                    }
                    lock (sending)
                    {
                        client = c;
                        stream = c.GetStream();
                    }
                    connected = true;
                    Send(EngineProtocol.Hello());
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string line;
                        while (!stopping && (line = reader.ReadLine()) != null)
                        {
                            var (type, message) = EngineProtocol.Read(line);
                            if (type != null)
                                incoming.Enqueue((type, message));
                        }
                    }
                }
                catch (Exception e) when (e is IOException || e is SocketException || e is AggregateException || e is ObjectDisposedException)
                {
                }
                finally
                {
                    connected = false;
                    lock (sending)
                    {
                        stream = null;
                        client?.Close();
                        client = null;
                    }
                }
                if (!stopping)
                    Thread.Sleep(500);
            }
        }

        public void Dispose()
        {
            stopping = true;
            lock (sending)
                client?.Close();
        }
    }
}
