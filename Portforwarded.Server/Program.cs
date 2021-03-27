using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Portforwarded.Server
{
    class Program
    {
        static IConfiguration Configuration;
        async static Task<int> Main(string[] args)
        {
            LogLine("Launching Portforwarded.Server, loading configuration");

            var config = LoadAndValidateConfig(args);
            if (!config.valid)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                LogLine("Configuration not valid, reasons stated below:", ConsoleColor.Red);
                config.errors.ForEach(l => LogLine(l, ConsoleColor.Red));

                return -1;
            }

            if (!ExecutableExists())
            {
                LogLine("Configured executable or directory does not exist, please check the path and file variables", ConsoleColor.Red);
                return -1;
            }

            try
            {
                SetupPortforwarding();
                await LaunchProcess();
            }
            catch (Exception ex)
            {
                LogLine(ex.ToString(), ConsoleColor.Red);
            } finally
            {
                TeardownPortforwarding();
            }

            Console.ReadLine();
            return 0;
        }

        private static async Task LaunchProcess()
        {
            LogLine($"Launching process {Configuration["executable:file"]}");
            if (!string.IsNullOrWhiteSpace(Configuration["executable:parameters"]))
            {
                LogLine($"Using parameters: {Configuration["executable:parameters"]}");
            }

            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    WorkingDirectory = Configuration["executable:workingdirectory"],
                    FileName = Configuration["executable:file"],
                    Arguments = Configuration["executable:parameters"],
                    UseShellExecute = false,
                    RedirectStandardInput = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                process.OutputDataReceived += Process_OutputDataReceived;
                process.ErrorDataReceived += Process_ErrorDataReceived;

                process.Start();

                await process.WaitForExitAsync();
            }
        }

        private static void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            LogLine(e.Data, ConsoleColor.Red);
        }

        private static void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            LogLine(e.Data);
        }

        private static void SetupPortforwarding()
        {
            LogLine("Setting up portforwarding", ConsoleColor.Green);
        }

        private static void TeardownPortforwarding()
        {
            LogLine("Tearing down portforwarding", ConsoleColor.Green);
        }

        private static bool ExecutableExists()
        {
            if (!Directory.Exists(Configuration["executable:workingdirectory"]))
            {
                return false;
            }

            if (!File.Exists(Path.Combine(Configuration["executable:workingdirectory"], Configuration["executable:file"])))
            {
                return false;
            }

            return true;
        }

        private static (bool valid, List<string> errors) LoadAndValidateConfig(string[] args)
        {
            var errors = new List<string>();

            Configuration = new ConfigurationBuilder()
                            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                            .AddEnvironmentVariables()
                            .AddCommandLine(args)
                            .Build();

            if (string.IsNullOrEmpty(Configuration["executable:file"]))
            {
                errors.Add("- Missing option 'executable:file'");
            }

            if (string.IsNullOrWhiteSpace(Configuration["executable:workingdirectory"]))
            {
                errors.Add("- Missing option 'executable:workingdirectory'");
            }

            if (Configuration.GetSection("upnp") == null || !Configuration.GetSection("upnp").GetChildren().Any())
            {
                errors.Add("- Missing configuration for UPnP, needed for port forwarding");
            }

            return (!errors.Any(), errors);
        }

        internal static void LogLine(string line, ConsoleColor color = ConsoleColor.White)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(line);
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}
