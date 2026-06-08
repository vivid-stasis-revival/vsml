using System;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Threading.Tasks;

using vividstasisModLoader;

using static vividstasisModLoader.ConsoleOutput;

namespace vividstasisModLoader.TVOClientCommunicate
{
    class PipeClient
    {
        public static bool IPCMode { get; private set; }
        public static string GamePath { get; private set; }
        private static StreamWriter? _ipcWriter;

        public static void Init(string[] args)
        {
            if (args.Length > 0 && args[0] != "--dry-run")
            {
                var pipeClient = new NamedPipeClientStream(
                    ".",
                    args[0],
                    PipeDirection.InOut);

                pipeClient.Connect();
                
                _ipcWriter = new StreamWriter(pipeClient) { AutoFlush = true };

                PrintAppBanner("vividstasis 模组加载器 (IPC通信模式)", "vividstasis Mod Loader (IPC communicate mode)", AppInfo.VERSION, AppInfo.CHANGE_LOG);

                Console.WriteLine($"[VML IPC] 当前IPC传输模式: {pipeClient.TransmissionMode}.");

                // 不用 using 包裹 StreamReader，防止关闭基础流
                StreamReader sr = new StreamReader(pipeClient);
                
                // 临时存储读取到的IPC服务端发送数据
                string temp;

                // 从服务端等待SYNC消息，并在收到后读取下一行数据并输出到控制台
                try
                {
                    while (true)
                    {
                        // 从服务端等待SYNC消息
                        Console.WriteLine("[VML IPC] 等待消息...");
                        temp = sr.ReadLine();

                        if (temp == null)
                            ExitWithServer("退出");

                        if (!temp.StartsWith("SYNC"))
                            continue;

                        // 读取服务端数据并输出到控制台
                        temp = sr.ReadLine();

                        if (temp == null)
                            ExitWithServer("退出");

                        if (temp == "EXIT")
                            ExitWithServer("退出");
                        else if (temp.StartsWith("_tvocmsg_gamepath"))
                        {
                            Console.WriteLine($"[VML IPC] 获取到游戏路径: {temp.Substring(18)}");
                            IPCMode = true;
                            GamePath = temp.Substring(18);
                            return;
                        }

                        Console.WriteLine("[VML IPC] 获取到服务端数据: " + temp);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[VML IPC] 发生不可预料的错误: " + ex.Message);
                    ExitWithServer(ex.Message);
                }
            }
        }

        private static void ExitWithServer(string reason)
        {
            Console.WriteLine("[VML IPC] VML将关闭，原因: " + reason);
            Task.Delay(1000).Wait();
            Environment.Exit(0);
        }

        public static void SendMessage(string message)
        {
            try
            {
                if (_ipcWriter != null)
                {
                    _ipcWriter.WriteLine(message);
                    // Console.WriteLine("[VML IPC] 已发送消息到服务端: " + message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[VML IPC] 无法连接到IPC服务器，发送消息失败: " + ex.Message);
            }
        }
    }
}
