using System;
using System.IO;
using System.IO.Pipes;

using vividstasisModLoader;

using static vividstasisModLoader.ConsoleOutput;

namespace vividstasisModLoader.TVOClientCommunicate
{
    class PipeClient
    {
        private const string PipeName = "VividStasisVmlPipe";

        public static bool IPCMode { get; private set; }
        public static string GamePath { get; private set; } = string.Empty;
        private static NamedPipeClientStream? _pipe;
        private static StreamReader? _reader;
        private static StreamWriter? _writer;

        public static void Init(string[] args)
        {
            if (args.Length == 0 || args[0] != "ipc")
                return;

            try
            {
                PrintAppBanner("vividstasis 模组加载器 (IPC通信模式)", "vividstasis Mod Loader (IPC communicate mode)", AppInfo.VERSION, AppInfo.CHANGE_LOG);
                Console.Error.WriteLine("[VML IPC] Connecting to pipe...");

                _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                _pipe.Connect(5000);

                _reader = new StreamReader(_pipe);
                _writer = new StreamWriter(_pipe) { AutoFlush = true };

                Console.Error.WriteLine("[VML IPC] Connected, waiting for handshake...");

                while (true)
                {
                    string? line = _reader.ReadLine();
                    if (line == null)
                    {
                        ExitWithServer("pipe closed");
                        return;
                    }

                    if (line == "EXIT")
                    {
                        ExitWithServer("EXIT requested");
                        return;
                    }

                    if (line.StartsWith("_tvocmsg_gamepath"))
                    {
                        IPCMode = true;
                        GamePath = line.Substring(18);
                        Console.Error.WriteLine($"[VML IPC] Game path: {GamePath}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[VML IPC] Error: {ex.Message}");
            }
        }

        private static void ExitWithServer(string reason)
        {
            Console.Error.WriteLine($"[VML IPC] Exiting: {reason}");
            Shutdown();
            IPCMode = false;
            GamePath = string.Empty;
        }

        public static void SendMessage(string message)
        {
            if (!IPCMode || _writer == null) return;
            try { _writer.WriteLine(message); } catch { }
        }

        public static void SendException(Exception exception)
        {
            if (!IPCMode || _writer == null) return;
            try
            {
                var payload = SimpleJson.ToExceptionJson(
                    exception.GetType().FullName ?? exception.GetType().Name,
                    exception.Message,
                    exception.ToString()
                );

                _writer.WriteLine($"VML_EXCEPTION {payload}");
            }
            catch { }
        }

        public static void Shutdown()
        {
            if (!IPCMode) return;
            try
            {
                SendMessage("EXIT");
                Console.Error.WriteLine("[VML IPC] Shutting down pipe...");
            }
            catch { }
            try { _writer?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
            _reader = null;
            _writer = null;
            IPCMode = false;
            Console.Error.WriteLine("[VML IPC] Pipe closed.");
        }

        public static void SendException(Exception exception)
        {
            var payload = JsonSerializer.Serialize(new
            {
                exception_type = exception.GetType().FullName ?? exception.GetType().Name,
                message = exception.Message,
                details = exception.ToString()
            });

            SendMessage($"VML_EXCEPTION {payload}");
        }
    }
}
