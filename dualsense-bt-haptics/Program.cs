// See https://aka.ms/new-console-template for more information

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using dualsense_bt_haptics;
using HidApi;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using ZLogger;

using PVIGEM_CLIENT = System.IntPtr;
using PVIGEM_TARGET = System.IntPtr;

class Program
{
    private static ILogger logger;
    private static Device hid;
    private static MMDevice audio;
    private static ConcurrentQueue<byte> audioQueue;
    private static WasapiLoopbackCapture capture;
    private static PVIGEM_CLIENT vigem_client;
    private static PVIGEM_TARGET vigem_ds;
    private static byte packetCounter = 0;
    private static int SAMPLE_SIZE = 64;
    private static int SAMPLE_RATE = 3000;
    // var intervalNs = 1000000000L*SAMPLE_SIZE/(SAMPLE_RATE*2);
    private static int intervalMs = 1000 * SAMPLE_SIZE / (SAMPLE_RATE * 2);
    private static Stopwatch stopWatch;
    private static readonly object _hidLock = new object();
    
    private static void InitLogger()
    {
        var factory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);

            logging.AddZLoggerConsole();
            logging.AddZLoggerFile("log.txt");
        });
        
        logger = factory.CreateLogger("Program");
    }

    private static void InitHid(ushort vendorId,ushort  productId)
    {
        hid = new Device(vendorId, productId);
        // hid.SetNonBlocking(true);
        var manufacturer = hid.GetManufacturer();
        logger.ZLogInformation($"Manufacturer: {manufacturer}");
        var product = hid.GetProduct();
        logger.ZLogInformation($"Product: {product}");
        var serial = hid.GetSerialNumber();
        logger.ZLogInformation($"Serial Number: {serial}");
    }

    private static void InitAudioCapture()
    {
        audioQueue = new ConcurrentQueue<byte>();
        var enumerator = new MMDeviceEnumerator();
        foreach (var wasapi in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            logger.ZLogInformation($"Found device: {wasapi.FriendlyName}");

            if (wasapi.FriendlyName.Contains("DualSense Wireless Controller"))
            {
                audio = wasapi;
                break;
            }
        }

        if (audio == null) throw new Exception("Audio Device not found");

        logger.ZLogInformation($"Capture device: {audio.FriendlyName}");

        capture = new WasapiLoopbackCapture(audio);
        capture.WaveFormat = new WaveFormat(3000, 8, 2);
        
        capture.DataAvailable += (s, a) =>
        {
            // logger.ZLogDebug($"Received {a.BytesRecorded}");
            /*if(audioQueue.Count > 6400)
            {
                audioQueue.Clear();
            }*/
            for (int i = 0; i < a.BytesRecorded; i++)
            {
                audioQueue.Enqueue((byte)(a.Buffer[i] + 128)); // +128: s8 to u8
            }
        };
        capture.RecordingStopped += (s, a) =>
        {
            hid.Dispose();
            capture.Dispose();
        };
    }

    private static void InitViGEm()
    {
        vigem_client = ViGEmClient.vigem_alloc();
        ViGEmClient.VIGEM_ERROR error = ViGEmClient.vigem_connect(vigem_client);
        switch (error)
        {
            case ViGEmClient.VIGEM_ERROR.VIGEM_ERROR_ALREADY_CONNECTED:
                throw new Exception("VIGEM_ALREADY_CONNECTED");
            case ViGEmClient.VIGEM_ERROR.VIGEM_ERROR_BUS_NOT_FOUND:
                throw new Exception("VIGEM_BUS_NOT_FOUND");
            case ViGEmClient.VIGEM_ERROR.VIGEM_ERROR_BUS_ACCESS_FAILED:
                throw new Exception("VIGEM_BUS_ACCESS_FAILED");
            case ViGEmClient.VIGEM_ERROR.VIGEM_ERROR_BUS_VERSION_MISMATCH:
                throw new Exception("VIGEM_BUS_VERSION_MISMATCH");
        }
        
        vigem_ds = ViGEmClient.vigem_target_ds5_alloc();

        error = ViGEmClient.vigem_target_add(vigem_client,vigem_ds);
        switch (error)
        {
            case ViGEmClient.VIGEM_ERROR.VIGEM_ERROR_NONE:
                break;
            case ViGEmClient.VIGEM_ERROR.VIGEM_ERROR_BUS_NOT_FOUND:
                throw new Exception("VIGEM_BUS_NOT_FOUND");
            case ViGEmClient.VIGEM_ERROR.VIGEM_ERROR_TARGET_UNINITIALIZED:
                throw new Exception("VIGEM_TARGET_UNINITIALIZED");
            case ViGEmClient.VIGEM_ERROR.VIGEM_ERROR_ALREADY_CONNECTED:
                throw new Exception("VIGEM_ALREADY_CONNECTED");
            case ViGEmClient.VIGEM_ERROR.VIGEM_ERROR_NO_FREE_SLOT:
                throw new Exception("VIGEM_NO_FREE_SLOT");
            default:
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
    
    static void WinmmCallback(uint id, uint msg, IntPtr user, IntPtr dw1, IntPtr dw2)
    {
        if (stopWatch.ElapsedMilliseconds > intervalMs + 3)
        {
            logger.ZLogWarning($"Warning: lag detected. {stopWatch.ElapsedMilliseconds} ms");
        }
    
        stopWatch.Restart();
        if (audioQueue.Count < 64)
        {
            // logger.ZLogTrace($"audioQueue is empty");
            return;
        }
    
        byte[] data = new byte[142];
        data[0] = 0x32;
        data[1] = 0;
        // Packet 0x11
        data[2] = 0x11 | 0 << 6 | 1 << 7; // pid(0x11) unk(false) sized(true)
        data[3] = 7;
        data[4] = 0b11111110;
        data[5] = 0;
        data[6] = 0;
        data[7] = 0;
        data[8] = 0;
        data[9] = 0xFF;
        data[10] = packetCounter++;
        // Packet 0x12
        data[11] = 0x12 | 0 << 6 | 1 << 7;
        data[12] = (byte)SAMPLE_SIZE;
        for (int i = 13; i < SAMPLE_SIZE + 13; i++)
        {
            audioQueue.TryDequeue(out data[i]);
            // 静默时有底噪
        }
    
        var crc = Utils.crc32(data, data.Length - 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(data.Length - 4, 4), crc);
        lock(_hidLock) { hid.Write(data); }
    }

    static Task InputForwardTask()
    {
        var data = new byte[78];
        return Task.Run(async () =>
        {
            while (true)
            {
                int len = 0;
                lock(_hidLock) {len = hid.Read(data.AsSpan());}
                // logger.ZLogTrace($"Read Hid {len} bytes");
                if (len > 0)
                {
                    switch (data[0])
                    {
                        case 0x31:
                        {
                            logger.ZLogTrace($"Receive Input Report: {BitConverter.ToString(data).Replace("-","")}");
                            ViGEmClient.DS5_REPORT report;
                            report.Report = data.AsSpan(2,63).ToArray();

                            var error = ViGEmClient.vigem_target_ds5_update(vigem_client, vigem_ds, report);
                            if (error != ViGEmClient.VIGEM_ERROR.VIGEM_ERROR_NONE)
                            {
                                logger.ZLogError($"Fail to update input data. code:{error}");
                            }
                            break;
                        }
                        case 0x01:
                        {
                            logger.ZLogInformation($"Receive 0x01 Input Report: {BitConverter.ToString(data).Replace("-","")}");
                            break;
                        }
                    }
                }
                Thread.Sleep(4);
            }
        });
    }

    static Task OutputForwardTask()
    {
        ViGEmClient.DS5_AWAIT_OUTPUT_BUFFER outputBuffer = new ViGEmClient.DS5_AWAIT_OUTPUT_BUFFER();
        int outputSeq = 0;
        return Task.Run(async () =>
        {
            while (true)
            {
                var error = ViGEmClient.vigem_target_ds5_await_output_report_timeout(vigem_client, vigem_ds,100,ref outputBuffer);
                if (error == ViGEmClient.VIGEM_ERROR.VIGEM_ERROR_NONE)
                {
                    logger.ZLogInformation($"Receive Output Report");

                    byte[] outputData = new byte[78];
                    outputData[0] = 0x31;
                    outputData[1] = (byte)(outputSeq << 4);
                    if (++outputSeq == 256)
                    {
                        outputSeq = 0;
                    }
                    outputData[2] = 0x10;
                    Buffer.BlockCopy(outputBuffer.Buffer, 1, outputData, 3, 63);
                    var crc = Utils.crc32(outputData, outputData.Length - 4);
                    BinaryPrimitives.WriteUInt32LittleEndian(outputData.AsSpan(outputData.Length - 4, 4), crc);
                    lock(_hidLock) { hid.Write(outputData); }
                }
            }
        });
    }
    

    public static void Main(string[] args)
    { 
        InitLogger();
        // Connect To BT Dualsense
        InitHid(0x054C, 0x0CE6);
        InitViGEm();
        InitAudioCapture();
        stopWatch = new Stopwatch();
        
        logger.ZLogInformation($"Hello World");
        capture.StartRecording();
        stopWatch.Start();
        winmm.Start((uint)intervalMs, WinmmCallback);

        Task inputTask = InputForwardTask();
        Task outputTask = OutputForwardTask();

        while (capture.CaptureState != CaptureState.Stopped)
        {
            Thread.Sleep(500);
        }
    }
}



