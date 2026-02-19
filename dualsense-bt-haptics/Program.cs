// See https://aka.ms/new-console-template for more information

using System.Buffers.Binary;
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
    private static BufferedWaveProvider bufferedWaveProvider;
    private static WasapiCapture capture;
    private static PVIGEM_CLIENT vigem_client;
    private static PVIGEM_TARGET vigem_ds;
    private static byte packetCounter = 0;
    private static byte reportSeqCounter = 0;
    private static int SAMPLE_SIZE = 64;
    private static int SAMPLE_RATE = 3000;
    // var intervalNs = 1000000000L*SAMPLE_SIZE/(SAMPLE_RATE*2);
    private static int intervalMs = 1000 * SAMPLE_SIZE / (SAMPLE_RATE * 2);
    private static float GAIN = 2.0f;
    private static Stopwatch latency;
    private static Stopwatch lastAddSampleTime;
    private static readonly object _hidLock = new object();
    private static byte[] stateData = new byte[47];
    
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
        var enumerator = new MMDeviceEnumerator();
        foreach (var wasapi in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            logger.ZLogInformation($"Found device: {wasapi.FriendlyName}");

            // DualSense Wireless Controller
            if (wasapi.FriendlyName.Contains("DualSense Wireless Controller"))
            {
                audio = wasapi;
                break;
            }
        }

        if (audio == null) throw new Exception("Audio Device not found");

        logger.ZLogInformation($"Capture device: {audio.FriendlyName}");

        // capture = new WasapiCapture(audio,true,0);
        capture = new WasapiLoopbackCapture(audio);
        capture.WaveFormat = new WaveFormat(SAMPLE_RATE, 8, 2);
        WasapiOut playbackDevice = null;
        
        capture.DataAvailable += (s, a) =>
        {
            logger.ZLogDebug($"Received {a.BytesRecorded}");
            bufferedWaveProvider.AddSamples(a.Buffer, 0, a.BytesRecorded);
            if (a.BytesRecorded > 0)
            {
                lastAddSampleTime.Restart();
            }
        };
        
        bufferedWaveProvider = new BufferedWaveProvider(capture.WaveFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(5), // 缓冲最多5秒
            DiscardOnBufferOverflow = true          // 防止内存爆炸
        };

        // 初始化播放设备 用于同时播放来测试延迟
        /*playbackDevice = new WasapiOut(AudioClientShareMode.Shared, 10); // 10 latency ms
        playbackDevice.Init(bufferedWaveProvider);
        playbackDevice.Play();*/
        
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
        if (latency.ElapsedMilliseconds > intervalMs + 3)
        {
            logger.ZLogWarning($"Warning: lag detected. {latency.ElapsedMilliseconds} ms");
        }
        /*if (bufferedWaveProvider.BufferedBytes > 600)
        {
            byte[] skipBuffer = new byte[bufferedWaveProvider.BufferedBytes - 128]; 
            bufferedWaveProvider.Read(skipBuffer, 0, skipBuffer.Length);
            logger.ZLogWarning($"Sync: Dropping old samples to catch up");
        }*/
    
        latency.Restart();
        if (bufferedWaveProvider.BufferedBytes < 64 && lastAddSampleTime.ElapsedMilliseconds <= 100)
        {
            return;
        }
    
        byte[] data = new byte[206]; // 0x33:206 0x32:142
        // 似乎与ReportId无关，主要靠的是packetId，尝试把0x32换成0x35同样可以工作
        data[0] = 0x33; // 感觉 0x32 和 0x33 差不多，不知道为什么它要用33
        data[1] = (byte)(reportSeqCounter << 4);
        reportSeqCounter = (byte)((reportSeqCounter + 1) & 0x0F);
        // Packet 0x11
        data[2] = 0x11 | 0 << 6 | 1 << 7; // pid(0x11) unk(false) sized(true)
        data[3] = 7;
        data[4] = 0b11111110;
        /*data[5] = 0;
        data[6] = 0;
        data[7] = 0;
        data[8] = 0;
        data[9] = 0xFF;*/
        // 来自DSX的神秘参数？
        data[5] = 0x40;
        data[6] = 0x40;
        data[7] = 0x40;
        data[8] = 0x40;
        data[9] = 0x40;
        data[10] = packetCounter++;
        // Packet 0x12
        data[11] = 0x12 | 0 << 6 | 1 << 7;
        data[12] = (byte)SAMPLE_SIZE;
        bufferedWaveProvider.Read(data, 13, SAMPLE_SIZE);
        for (int i = 13; i < SAMPLE_SIZE + 13; i++)
        {
            data[i] = (byte)((data[i] - 128) * GAIN);
        }
        // 来自 DSX，应该就是普通的SetStateData
        var packet_0x10 = new byte[]
        {
            0x90, // Packet: 0x10
            0x3f, // 63
            // Length: 47 ⬇️
            // SetStateData 
            0xfd, 0xf7, 0x0, 0x0, 0x7f, 0x7f,
            0xff, 0x9, 0x0, 0xf, 0x0, 0x0, 0x0, 0x0,
            0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0,
            0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0,
            0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0xa,
            0x7, 0x0, 0x0, 0x2, 0x1, 
            0x00,
            0x00,0x9b,0x00 // RGB LED: R, G, B
        };
        // Array.Copy(packet_0x10, 0, data, 77, packet_0x10.Length);
        data[77] = 0x90;
        data[78] = 0x3f;
        Array.Copy(stateData, 0, data, 79, stateData.Length);
        
    
        var crc = Utils.crc32(data, data.Length - 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(data.Length - 4, 4), crc);
        lock(_hidLock) { hid.Write(data); }
        
        if (bufferedWaveProvider.BufferedBytes >= 64)
        {
            // 立即发送数据，减少数据积压
            WinmmCallback(id, msg, user, dw1, dw2);
        }
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
                Thread.Sleep(5);
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
                    Buffer.BlockCopy(outputBuffer.Buffer, 1, outputData, 3, 47);
                    Buffer.BlockCopy(outputBuffer.Buffer, 1, stateData, 0, 47);
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
        latency = new Stopwatch();
        lastAddSampleTime = new Stopwatch();
        
        logger.ZLogInformation($"Hello World");
        // winmm.timeBeginPeriod(1);
        capture.StartRecording();
        latency.Start();
        lastAddSampleTime.Start();
        winmm.Start((uint)intervalMs, WinmmCallback);

        Task inputTask = InputForwardTask();
        Task outputTask = OutputForwardTask();

        while (capture.CaptureState != CaptureState.Stopped)
        {
            Thread.Sleep(500);
        }
    }
}



