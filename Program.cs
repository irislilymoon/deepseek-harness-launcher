using System;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace DeepSeekRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Starting DeepSeek Harness...");
            ProcessStartInfo npxInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c npx @deepseek-ai/dsh web",
                UseShellExecute = false
            };
            Process npxProcess = Process.Start(npxInfo);

            Console.WriteLine("Waiting for server...");
            for (int i = 0; i < 30; i++)
            {
                Thread.Sleep(1000);
                try
                {
                    WebRequest request = WebRequest.Create("http://127.0.0.1:3080");
                    request.Timeout = 1000;
                    using (WebResponse response = request.GetResponse())
                    {
                        break;
                    }
                }
                catch (WebException)
                {
                }
            }

            Console.WriteLine("Opening browser...");
            Process.Start("http://127.0.0.1:3080");

            Console.WriteLine("Close this window to stop the server.");
            if (npxProcess != null) npxProcess.WaitForExit();
        }
    }
}
