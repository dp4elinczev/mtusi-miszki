using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpPcap;

namespace TcpConnectionMonitorService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try {
                var devices = CaptureDeviceList.Instance;

                if (devices.Count < 1) {
                    _logger.LogError("No devices were found on this machine");
                    return;
                }

                int i = 0;
                int readTimeoutMilliseconds = 1000;

                foreach (var device in devices) {
                    device.Open(DeviceMode.Promiscuous, readTimeoutMilliseconds);
                    _logger.LogInformation($"{i}) {device?.MacAddress?.ToString() ?? "none"} {device?.Description ?? "none"}");
                    i++;
                    device.Close();
                }

                var dev = devices[0];
                dev.Open(DeviceMode.Promiscuous, readTimeoutMilliseconds);

                while (!stoppingToken.IsCancellationRequested)
                {
                    RawCapture rawPacket = dev.GetNextPacket();

                    if (rawPacket == null) {
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    var frame = PacketDotNet.Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);

                    if (frame is PacketDotNet.EthernetPacket eth) {

                        var ip = frame.Extract<PacketDotNet.IPPacket>();

                        if (ip != null) {

                            var tcp = frame.Extract<PacketDotNet.TcpPacket>();

                            if (tcp != null) {
                                if (tcp.Synchronize && !tcp.Acknowledgment) {
                                    _logger.LogInformation($"Opening {ip.SourceAddress}:{tcp.SourcePort} -> {ip.DestinationAddress}:{tcp.DestinationPort}");
                                }
                                if (tcp.Finished) {
                                    _logger.LogInformation($"Closing {ip.SourceAddress}:{tcp.SourcePort} -> {ip.DestinationAddress}:{tcp.DestinationPort}");
                                }
                            }
                        }
                    }
                }
            }
            catch(Exception ex) {
                _logger.LogError($"Exceptions was occured: {ex.Message} StackTrace: {ex.StackTrace}");

            }
        }
    }
}
