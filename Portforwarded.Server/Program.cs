using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Portforwarded.Server
{
    internal class Program
    {
        public static volatile bool SENDING_CTRL_C_TO_CHILD = false;

        static IConfiguration Configuration;
        static Process process;
        static Open.Nat.NatDevice UPnPDevice;
        public static CancellationTokenSource CancellationTokenSource { get; private set; }

        static bool TestMode = false;

        static CancellationToken cancellationToken;

        async static Task<int> Main(string[] args)
        {
            CancellationTokenSource = new CancellationTokenSource();
            cancellationToken = CancellationTokenSource.Token;
            Console.CancelKeyPress += Console_CancelKeyPress;

            LogLine("Launching Portforwarded.Server, loading configuration");

            var (valid, errors) = LoadAndValidateConfig(args);
            if (!valid)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                LogLine("Configuration not valid, reasons stated below:", ConsoleColor.Red);
                errors.ForEach(l => LogLine(l, ConsoleColor.Red));

                return -1;
            }

            UPnPDevice = await GetUPnPDevice();
            if (UPnPDevice == null)
            {
                LogLine("No UPnP device found, exiting.", ConsoleColor.Red);
                return -1;
            }

            if (!WorkingDirectoryExists())
            {
                LogLine("Configured executable or directory does not exist, please check the path and file variables", ConsoleColor.Red);
                return -1;
            }

            try
            {
                await SetupPortforwarding();
                if (!TestMode)
                {
                    await LaunchProcess();
                }
                else
                {
                    LogLine("Running in test mode, skipping process launch", ConsoleColor.Yellow);
                    await Task.Delay(5000);
                }
            }
            catch (Exception ex)
            {
                LogLine(ex.ToString(), ConsoleColor.Red);
            }
            finally
            {
                CancellationTokenSource.Dispose();
                await TeardownPortforwarding();
            }

            return 0;
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            if (TestMode || (process != null && !process.HasExited))
            {
                CancellationTokenSource.CancelAfter(5_000);

                process?.CancelOutputRead();
                process?.CancelErrorRead();
                process?.CloseMainWindow();
                process?.Kill(true);
            }

            LogLine("Exiting process", ConsoleColor.Red);
            e.Cancel = true;
        }

        private static async Task LaunchProcess()
        {
            LogLine($"Launching process {Configuration["executable:file"]}");
            if (!string.IsNullOrWhiteSpace(Configuration["executable:parameters"]))
            {
                LogLine($"Using parameters: {Configuration["executable:parameters"]}");
            }

            LogLine();

            using (process = new Process())
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

                process.BeginErrorReadLine();
                process.BeginOutputReadLine();

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

        /// <summary>
        /// Fetches a UPnP device from the network
        /// </summary>
        /// <returns></returns>
        private static async Task<Open.Nat.NatDevice> GetUPnPDevice()
        {
            var discoverer = new Open.Nat.NatDiscoverer();
            return await discoverer.DiscoverDeviceAsync(Open.Nat.PortMapper.Upnp, CancellationTokenSource);
        }

        /// <summary>
        /// Used to get the local IP address, that is on the same network as the UPnP device
        /// </summary>
        /// <param name="upnpDevice"></param>
        /// <returns></returns>
        private static IPAddress GetLocalIP(Open.Nat.NatDevice upnpDevice)
        {
            var hostEntries = Dns.GetHostEntry(Dns.GetHostName());
            var ipAddresses = hostEntries.AddressList.Select(i => i.ToString());

            var upnpDeviceEndpoint = upnpDevice.ToString().Split("\n").FirstOrDefault(d => d.Contains("EndPoint"));

            var endpointAddress = string.Join('.', upnpDeviceEndpoint.Replace("EndPoint: ", "").Trim().Split('.').Take(2)) + ".";
            var matchingIPAddress = ipAddresses.FirstOrDefault(a => a.StartsWith(endpointAddress));

            if (string.IsNullOrWhiteSpace(matchingIPAddress)) return null;

            return IPAddress.Parse(matchingIPAddress);
        }

        private static async Task SetupPortforwarding()
        {
            LogLine("Setting up portforwarding", ConsoleColor.Green);
            LogLine();

            var forwardMapping = GetForwardMappingFromConfig();
            foreach (var map in forwardMapping)
            {
                //await UPnPDevice.DeletePortMapAsync(map);
                await UPnPDevice.CreatePortMapAsync(map);

                LogLine($"Created map for IP: {map.PrivateIP} (Local port: {map.PrivatePort}, Public port: {map.PublicPort}, Protocol: {map.Protocol})");
            }

            await GetCurrentMappings();
        }

        private static async Task TeardownPortforwarding()
        {
            LogLine("Tearing down portforwarding", ConsoleColor.Green);
            LogLine();

            var forwardMapping = GetForwardMappingFromConfig();
            foreach (var map in forwardMapping)
            {
                await UPnPDevice.DeletePortMapAsync(map);

                LogLine($"Removed map for IP: {map.PrivateIP} (Local port: {map.PrivatePort}, Public port: {map.PublicPort}, Protocol: {map.Protocol})");
            }

            await GetCurrentMappings();
        }

        private static async Task GetCurrentMappings()
        {
            var currentMappings = await UPnPDevice.GetAllMappingsAsync();

            LogLine();

            LogLine("Currently mapped ports and IP addresses", ConsoleColor.Cyan);
            LogLine();
            foreach (var map in currentMappings)
            {
                LogLine($"Map: {map.PrivateIP}->{map.PublicIP} ({map.PrivatePort}->{map.PublicPort}) Expiration: {map.Expiration}");
            }
        }

        private static IEnumerable<Open.Nat.Mapping> GetForwardMappingFromConfig()
        {
            return Configuration.GetSection("upnp").Get<List<ForwardMapping>>().Select(GetMapping);
        }

        private static Open.Nat.Mapping GetMapping(ForwardMapping map)
        {
            var localIp = string.IsNullOrWhiteSpace(map.LocalIPAddress) ? UPnPDevice.LocalAddress : IPAddress.Parse(map.LocalIPAddress);
            var mapping = new Open.Nat.Mapping(map.Protocol, localIp, map.LocalPort, map.PublicPort, 0, $"Portforwarded.Server ({map.LocalPort}->{map.PublicPort})");
            return mapping;
        }

        private static bool WorkingDirectoryExists()
        {
            return Directory.Exists(Configuration["executable:workingdirectory"]);
        }

        private static (bool valid, List<string> errors) LoadAndValidateConfig(string[] args)
        {
            var errors = new List<string>();

            Configuration = new ConfigurationBuilder()
                            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                            .AddEnvironmentVariables()
                            .AddCommandLine(args)
                            .Build();

            // Check if the configuration is set to testing mode, and if so, ignore missing executable and working directory
            TestMode = Configuration["testmode"] == "true";

            if (!TestMode)
            {
                if (string.IsNullOrEmpty(Configuration["executable:file"]))
                {
                    errors.Add("- Missing option 'executable:file'");
                }

                if (string.IsNullOrWhiteSpace(Configuration["executable:workingdirectory"]))
                {
                    errors.Add("- Missing option 'executable:workingdirectory'");
                }
            }

            if (Configuration.GetSection("upnp") == null || !Configuration.GetSection("upnp").GetChildren().Any())
            {
                errors.Add("- Missing configuration for UPnP, needed for port forwarding");
            }

            return (!errors.Any(), errors);
        }

        internal static void LogLine(string line = "", ConsoleColor color = ConsoleColor.White)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(line);
            Console.ForegroundColor = ConsoleColor.White;
        }
    }

    internal class ForwardMapping
    {
        public Open.Nat.Protocol Protocol { get; set; }
        public string LocalIPAddress { get; set; }
        public int LocalPort { get; set; }
        public int PublicPort { get; set; }
    }
}
