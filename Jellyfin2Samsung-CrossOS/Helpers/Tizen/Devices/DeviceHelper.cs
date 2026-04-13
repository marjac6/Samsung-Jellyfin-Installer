using System;
using Jellyfin2Samsung.Helpers.API;
using Jellyfin2Samsung.Interfaces;
using Jellyfin2Samsung.Models;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers.Tizen.Devices
{
    public class DeviceHelper
    {
        private readonly INetworkService _networkService;
        private readonly TizenApiClient _tizenApiClient;

        public DeviceHelper(
            INetworkService networkService,
            TizenApiClient tizenApiClient)
        {
            _networkService = networkService;
            _tizenApiClient = tizenApiClient;
        }



        public async Task<List<NetworkDevice>> ScanForDevicesAsync(CancellationToken cancellationToken = default, bool virtualScan = false)
        {
            var devices = new List<NetworkDevice>();
            var networkDevices = await _networkService.GetLocalTizenAddresses(cancellationToken, virtualScan);

            foreach (NetworkDevice device in networkDevices)
            {
                // Check for cancellation before processing each device
                cancellationToken.ThrowIfCancellationRequested();

                if (await _networkService.IsPortOpenAsync(device.IpAddress, 8001, cancellationToken))
                {
                    try
                    {
                        var samsungDevice = await _tizenApiClient.GetDeveloperInfoAsync(device);
                        if (!string.IsNullOrEmpty(samsungDevice.DeviceName))
                            devices.Add(samsungDevice);
                    }
                    catch
                    {
                        Trace.WriteLine($"Failed to get developer info for device at {device.IpAddress}.");
                    }
                }
                else
                {
                    try
                    {
                        device.ModelName = device.ModelName;
                        device.Manufacturer = device.Manufacturer;
                        device.DeveloperMode = "1";
                        device.DeveloperIP = string.Empty;

                        devices.Add(device);
                    }
                    catch { }
                }
            }

            // Dodaj wykrywanie emulatorów przez sdb devices

            try
            {
                var sdbPath = "sdb";
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = sdbPath,
                        Arguments = "devices",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit();

                // Loguj wyjście do pliku debug_... w katalogu Logs
                try
                {
                    string exeDir = System.AppContext.BaseDirectory;
                    string logFolder = System.IO.Path.Combine(exeDir, "Logs");
                    System.IO.Directory.CreateDirectory(logFolder);
                    string logFilePath = System.IO.Path.Combine(logFolder, $"debug_sdb_devices_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.log");
                    await System.IO.File.WriteAllTextAsync(logFilePath, output);
                }
                catch { }

                // Parsuj każdą linię zaczynającą się od emulator-
                foreach (var line in output.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("emulator-"))
                    {
                        var parts = trimmed.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 1)
                        {
                            string emulatorId = parts[0];
                            devices.Add(new NetworkDevice
                            {
                                IpAddress = emulatorId,
                                Manufacturer = "Tizen",
                                DeviceName = emulatorId,
                                ModelName = "Emulator",
                                DeveloperMode = "1",
                                DeveloperIP = emulatorId
                            });
                        }
                    }
                }
            }
            catch { }

            return devices;
        }
    }
}
