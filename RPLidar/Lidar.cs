﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using NLog;

namespace RPLidar
{
    /// <summary>
    /// RPLidar device
    /// </summary>
    public partial class Lidar
    {
        /// <summary>
        /// Command byte
        /// </summary>
        private enum Command : byte
        {
            GetInfo = 0x50,
            GetHealth = 0x52,
            GetConfig = 0x84,
            Stop = 0x25,
            Reset = 0x40,
            Scan = 0x20,
            ExpressScan = 0x82,
        }

        /// <summary>
        /// Config type word
        /// </summary>
        private enum ConfigType : uint
        {
            ScanModeCount = 0x70,
            ScanModeUsPerSample = 0x71,
            ScanModeMaxDistance = 0x74,
            ScanModeAnswerType = 0x75,
            ScanModeTypical = 0x7C,
            ScanModeName = 0x7F,
        }

        // Constants
        private const byte SyncByte = 0xA5;
        private const byte SyncByte2 = 0x5A;
        private const int DescriptorLength = 7;
        private readonly Descriptor InfoDescriptor = new Descriptor(20, true, 0x04);
        private readonly Descriptor HealthDescriptor = new Descriptor(3, true, 0x06);
        private readonly Descriptor LegacyScanDescriptor = new Descriptor(5, false, 0x81);
        private readonly Descriptor ExpressLegacyScanDescriptor = new Descriptor(84, false, 0x82);

        // Variables
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private readonly SerialPort port;

        /// <summary>
        /// Constructor
        /// </summary>
        public Lidar()
        {
            port = new SerialPort()
            {
                ReadTimeout = 500,
                ReadBufferSize = 32768,
                BaudRate = 115200
            };
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="portName">Port name</param>
        /// <param name="baudRate">Baud rate</param>
        /// <param name="readBufferSize">Read buffer size in bytes</param>
        public Lidar(string portName = "", int baudRate = 115200, int readBufferSize = 4096)
            : this()
        {
            PortName = portName;
            Baudrate = baudRate;
            ReadBufferSize = readBufferSize;
        }

        /// <summary>
        /// Port name
        /// </summary>
        public string PortName
        {
            get => port.PortName;
            set => port.PortName = value;
        }

        /// <summary>
        /// Baud rate
        /// </summary>
        public int Baudrate
        {
            get => port.BaudRate;
            set => port.BaudRate = value;
        }

        /// <summary>
        /// Read buffer size
        /// </summary>
        public int ReadBufferSize
        {
            get => port.ReadBufferSize;
            set => port.ReadBufferSize = value;
        }

        /// <summary>
        /// Receive timeout in milliseconds
        /// </summary>
        public int ReceiveTimeout
        {
            get => port.ReadTimeout;
            set => port.ReadTimeout = value;
        }

        /// <summary>
        /// Is port open ?
        /// </summary>
        public bool IsOpen
        {
            get
            {
                try
                {
                    return port.IsOpen;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error at checking port status.");
                    return false;
                }
            }
        }

        /// <summary>
        /// Angle offset in degrees
        /// This value is added to the measurements angle
        /// </summary>
        public float AngleOffset { get; set; }

        /// <summary>
        /// True if lidar is flipped (upside down), false it it's in upright position
        /// </summary>
        public bool IsFlipped { get; set; }

        /// <summary>
        /// Angle multiplier
        /// </summary>
        private float AngleMultiplier => IsFlipped ? -1.0f : 1.0f;

        /// <summary>
        /// Try to open lidar port
        /// </summary>
        /// <returns>true if port was opened, false if it failed</returns>
        public bool Open()
        {
            try
            {
                if (!port.IsOpen)
                {
                    port.Open();
                }

                return port.IsOpen;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error at opening port.");
                return false;
            }
        }

        /// <summary>
        /// Try to close lidar port
        /// </summary>
        /// <returns>true if port was closed, false if it failed</returns>
        public bool Close()
        {
            try
            {
                if (port.IsOpen)
                {
                    port.Close();
                }

                return !port.IsOpen;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error at closing port.");
                return false;
            }
        }

        /// <summary>
        /// Get number of bytes available for reading from port
        /// </summary>
        private bool GetBytesToRead(out int count)
        {
            try
            {
                count = port.BytesToRead;
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error at checking readable bytes count.");
                count = 0;
                return false;
            }
        }

        /// <summary>
        /// Timestamp in milliseconds
        /// </summary>
        public long Timestamp => Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);

        /// <summary>
        /// Control motor via serial DTR pin
        /// </summary>
        /// <param name="onOff">true to turn on motor, false to turn off</param>
        public void ControlMotorDtr(bool onOff)
        {
            port.DtrEnable = !onOff;
        }

        /// <summary>
        /// Send command
        /// </summary>
        /// <param name="command">Command</param>
        /// <param name="commandName">Name of the command</param>
        /// <returns>true if sent, false if failed</returns>
        private bool SendCommand(Command command, string commandName)
        {
            try
            {
                port.Write(new byte[2] { SyncByte, (byte)command }, 0, 2);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error at sending {commandName} command.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Send command
        /// </summary>
        /// <param name="command">Command</param>
        /// <param name="payload">Payload</param>
        /// <param name="commandName">Name of the command</param>
        /// <returns>true if sent, false if failed</returns>
        private bool SendCommand(Command command, byte[] payload, string commandName)
        {
            byte[] data = new byte[4 + payload.Length];
            byte checksum = 0;

            data[0] = SyncByte;
            data[1] = (byte)command;
            data[2] = (byte)payload.Length;
            Array.Copy(payload, 0, data, 3, payload.Length);

            for (int i = 0; i < data.Length; i++)
            {
                checksum ^= data[i];
            }

            data[3 + payload.Length] = checksum;

            try
            {
                port.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error at sending command {commandName}.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Read descriptor
        /// </summary>
        /// <param name="descriptor">Descriptor</param>
        /// <param name="responseName">Name of the response</param>
        /// <returns>true if succeeded, false on failure</returns>
        private bool ReadDescriptor(out Descriptor descriptor, string responseName)
        {
            List<byte> queue = new List<byte>();
            byte[] buffer = new byte[64];
            int missingBytes;
            int bytesRead;
            long startTime = Timestamp;

            descriptor = null;

            while (true)
            {
                // Try to receive as many bytes as are missing from complete packet
                missingBytes = DescriptorLength - queue.Count;
                try
                {
                    bytesRead = port.Read(buffer, 0, missingBytes);
                }
                catch (TimeoutException)
                {
                    logger.Error($"Timeout at receiving descriptor for {responseName}.");
                    return false;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Error at receiving descriptor for {responseName}.");
                    return false;
                }

                // Add bytes to the queue and check if we have complete descriptor
                queue.AddRange(buffer.Take(bytesRead));
                while (queue.Count >= DescriptorLength)
                {
                    if ((queue[0] == SyncByte) && (queue[1] == SyncByte2))
                    {
                        // Seems like we got our descriptor
                        // TODO Should consider with lengths above 255 ?
                        descriptor = new Descriptor(queue[2], queue[5] == 0, queue[6]);
                        return true;
                    }
                    else
                    {
                        // Pop first byte and check for sync bytes again
                        queue.RemoveAt(0);
                    }
                }

                // Timeout ?
                if ((Timestamp - startTime) > ReceiveTimeout)
                {
                    logger.Error($"Timeout at receiving descriptor for {responseName}.");
                    return false;
                }
            }
        }

        /// <summary>
        /// Check read descriptor against expected
        /// </summary>
        /// <param name="readDescriptor">Read descriptor</param>
        /// <param name="expectedDescriptor">Expected descriptor</param>
        /// <param name="responseName">Name of the response</param>
        /// <returns>true if descriptor received, false if not</returns>
        private bool CheckDescriptor(Descriptor readDescriptor, Descriptor expectedDescriptor, string responseName)
        {
            if ((expectedDescriptor.Length >= 0) && (expectedDescriptor.Length != readDescriptor.Length))
            {
                logger.Error($"Expected {responseName} descriptor length {expectedDescriptor.Length}, got {readDescriptor.Length}.");
                return false;
            }

            if (expectedDescriptor.IsSingle != readDescriptor.IsSingle)
            {
                logger.Error($"Expected {responseName} descriptor single to be {expectedDescriptor.IsSingle}, got {readDescriptor.IsSingle}.");
                return false;
            }

            if (expectedDescriptor.DataType != readDescriptor.DataType)
            {
                logger.Error($"Expected {responseName} descriptor data type 0x{expectedDescriptor.DataType:X2}, got 0x{readDescriptor.DataType:X2}.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Wait for descriptor
        /// </summary>
        /// <param name="expectedDescriptor">Descriptor to wait for</param>
        /// <param name="responseName">Name of the response</param>
        /// <returns>true if descriptor received, false if not</returns>
        private bool WaitForDescriptor(Descriptor expectedDescriptor, string responseName)
        {
            if (!ReadDescriptor(out Descriptor readDescriptor, responseName)) return false;
            return CheckDescriptor(readDescriptor, expectedDescriptor, responseName);
        }

        /// <summary>
        /// Read response
        /// </summary>
        /// <param name="length">Number of bytes to wait for</param>
        /// <param name="data">Received data bytes</param>
        /// <param name="responseName">Name of the response</param>
        /// <returns>true if response data received, false if not</returns>
        private bool ReadResponse(int length, out byte[] data, string responseName)
        {
            int dataIndex = 0;
            int bytesRead;
            long startTime = Timestamp;
            data = new byte[length];

            // Get required number of data bytes
            while (dataIndex < length)
            {
                try
                {
                    bytesRead = port.Read(data, dataIndex, length - dataIndex);
                }
                catch (TimeoutException)
                {
                    logger.Error($"Timeout at receiving data for {responseName}.");
                    return false;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Error at receiving data for {responseName}.");
                    return false;
                }

                dataIndex += bytesRead;

                // Timeout ?
                if ((Timestamp - startTime) > ReceiveTimeout)
                {
                    logger.Error($"Timeout at receiving data for {responseName}.");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Flush input buffer
        /// </summary>
        private bool FlushInput()
        {
            try
            {
                port.DiscardInBuffer();
                port.BaseStream.Flush();
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error on flushing input buffer.");
                return false;
            }
        }

        /// <summary>
        /// Reset lidar
        /// 
        /// Note: RPLidar specification says that after reset it takes only 2 ms until lidar is ready to receive
        ///       new commands, however practical tests show that it takes about 700 ms and it also sends out
        ///       it's version info which must be read or flushed to avoid disturbing following requests.
        /// </summary>
        /// <param name="waitTime">Number of milliseconds to wait until lidar has restarted</param>
        /// <returns>true if success, false if not</returns>
        public bool Reset(int waitTime = 700)
        {
            // Send reset command
            if (!SendCommand(Command.Reset, "reset")) return false;

            // Flush inputs little bit after sending reset command because of the full-duplex and asyncrhonous operation of the serial interface
            Thread.Sleep(10);
            FlushInput();
            ClearScanBuffer();

            // This is the delay to let lidar truly start up
            if (waitTime > 10)
            {
                Thread.Sleep(waitTime - 10);
            }

            // If there's something in the input buffer then read it all out and display it
            if (GetBytesToRead(out int length))
            {
                if (length > 0)
                {
                    ReadResponse(length, out byte[] data, "reset response");
                    // Remove line breaks
                    string msg = ASCIIEncoding.ASCII.GetString(data, 0, data.Length).Replace("\r\n",  " ");
                    logger.Info($"Reset message: {msg}");
                }
            }

            return true;
        }

        /// <summary>
        /// Get info
        /// </summary>
        /// <param name="info">Lidar info</param>
        /// <returns>true if success, false if not</returns>
        public bool GetInfo(out LidarInfo info)
        {
            info = null;

            if (!SendCommand(Command.GetInfo, "get info")) return false;
            if (!WaitForDescriptor(InfoDescriptor, "get info")) return false;
            if (!ReadResponse(InfoDescriptor.Length, out byte[] data, "get info")) return false;

            info = new LidarInfo()
            {
                Model = data[0],
                Firmware = $"{data[2]}.{data[1]}",
                Hardware = data[3].ToString(),
                SerialNumber = BitConverter.ToString(data, 4).Replace("-", string.Empty)
            };

            return true;
        }

        /// <summary>
        /// Get health
        /// </summary>
        /// <param name="status">Lidar health status</param>
        /// <param name="errorCode">Possible error code</param>
        /// <returns>true if success, false if not</returns>
        public bool GetHealth(out HealthStatus status, out ushort errorCode)
        {
            status = HealthStatus.Unknown;
            errorCode = 0;

            if (!SendCommand(Command.GetHealth, "get health")) return false;
            if (!WaitForDescriptor(HealthDescriptor, "get health")) return false;
            if (!ReadResponse(HealthDescriptor.Length, out byte[] data, "get health")) return false;

            status = (HealthStatus)data[0];
            errorCode = BitConverter.ToUInt16(data, 1);

            return true;
        }

        /// <summary>
        /// Get lidar configuration type
        /// </summary>
        /// <param name="configType">Configuration type</param>
        /// <param name="requestPayload">Extra request payload bytes</param>
        /// <param name="expectedResponseLength">Expected response payload length</param>
        /// <param name="responsePayload">Response payload bytes</param>
        /// <returns>true if success, false if not</returns>
        private bool GetConfigurationType(ConfigType configType, byte[] requestPayload, int? expectedResponseLength, out byte[] responsePayload)
        {
            string responseName = "get config";
            responsePayload = Array.Empty<byte>();

            Descriptor expectedDescriptor = new Descriptor(-1, true, 0x20);
            if (expectedResponseLength.HasValue)
            {
                expectedDescriptor.Length = expectedResponseLength.Value + 4;
            }

            if (!SendCommand(Command.GetConfig, BitConverter.GetBytes((uint)configType).Concat(requestPayload).ToArray(), responseName)) return false;
            if (!ReadDescriptor(out Descriptor readDescriptor, responseName)) return false;
            if (!CheckDescriptor(readDescriptor, expectedDescriptor, responseName)) return false;
            if (!ReadResponse(readDescriptor.Length, out byte[] responseRaw, responseName)) return false;

            // Verify response type
            uint responseType = BitConverter.ToUInt32(responseRaw, 0);
            if (responseType != (byte)configType)
            {
                logger.Error($"Expected {responseName} response type 0x{configType:X2}, got 0x{responseType:X2}.");
                return false;
            }

            // Get response payload bytes only
            responsePayload = new byte[responseRaw.Length - 4];
            Array.Copy(responseRaw, 4, responsePayload, 0, responseRaw.Length - 4);

            return true;
        }

        /// <summary>
        /// Get lidar configuration
        /// </summary>
        /// <param name="configuration">Configuration</param>
        /// <returns>true if success, false if not</returns>
        public bool GetConfiguration(out Configuration configuration)
        {
            configuration = new Configuration();

            // Get typical mode
            if (!GetConfigurationType(ConfigType.ScanModeTypical, Array.Empty<byte>(), 2, out byte[] response)) return false;
            configuration.Typical = BitConverter.ToUInt16(response, 0);

            // Get number of modes
            if (!GetConfigurationType(ConfigType.ScanModeCount, Array.Empty<byte>(), 2, out response)) return false;
            ushort count = BitConverter.ToUInt16(response, 0);

            // Create configurations of all modes
            configuration.Modes = new Dictionary<ushort, ScanModeConfiguration>();

            for (ushort mode = 0; mode < count; mode++)
            {
                ScanModeConfiguration modeConfiguration = new ScanModeConfiguration();
                byte[] modeBytes = BitConverter.GetBytes(mode);

                // Get name
                if (!GetConfigurationType(ConfigType.ScanModeName, modeBytes, null, out response))
                {
                    return false;
                }

                modeConfiguration.Name = Encoding.ASCII.GetString(response).TrimEnd('\0');

                // Get microseconds per sample
                if (!GetConfigurationType(ConfigType.ScanModeUsPerSample, modeBytes, 4, out response))
                {
                    return false;
                }

                modeConfiguration.UsPerSample = (float)BitConverter.ToUInt32(response, 0) / 256.0f;

                // Get maximum distance
                if (!GetConfigurationType(ConfigType.ScanModeMaxDistance, modeBytes, 4, out response))
                {
                    return false;
                }

                modeConfiguration.MaxDistance = (float)BitConverter.ToUInt32(response, 0) / 256.0f;

                // Ge answer type
                if (!GetConfigurationType(ConfigType.ScanModeAnswerType, modeBytes, 1, out response))
                {
                    return false;
                }

                modeConfiguration.AnswerType = response[0];

                // Add to list
                configuration.Modes.Add(mode, modeConfiguration);
            }

            return true;
        }

        /// <summary>
        /// Class to string
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return "Lidar";
        }
    }
}
