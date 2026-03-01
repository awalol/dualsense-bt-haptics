// See https://aka.ms/new-console-template for more information

using System.Buffers.Binary;
using System.CommandLine;
using System.Diagnostics;
using dualsense_bt_haptics;
using HidApi;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
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
    private static long intervalNs = 1000000000L*SAMPLE_SIZE/(SAMPLE_RATE*2);
    private static int intervalMs = 1000 * SAMPLE_SIZE / (SAMPLE_RATE * 2);
    private static float GAIN = 2.0f;
    private static Stopwatch latency;
    private static readonly object _hidLock = new object();
    private static byte[] stateData = new byte[47];
    
    // Command Args
    private static bool report33 = false;
    
    private static void InitLogger(bool verbose = false)
    {
        var factory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(verbose ? LogLevel.Trace : LogLevel.Information);

            logging.AddZLoggerConsole();
            logging.AddZLoggerFile("log.txt");
        });
        
        logger = factory.CreateLogger("Program");
        if (verbose)
        {
            logger.ZLogInformation($"Enable Verbose");
        }
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

        var report32 = new byte[142];
        report32[0] = 0x32;
        report32[1] = 0x10;
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
            0xff,0xd7,0x00 // RGB LED: R, G, B
        };
        Array.Copy(packet_0x10, 0, report32, 2, packet_0x10.Length);
        var crc = Utils.crc32(report32, report32.Length - 4);
        BinaryPrimitives.WriteUInt32LittleEndian(report32.AsSpan(report32.Length - 4, 4), crc);
        lock(_hidLock) { hid.Write(report32); }
    }

    static WdlResamplingSampleProvider resampler;
    static IWaveProvider sample16;
    
    private static void InitAudioCapture(int bufferDuration)
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
        Utils.SetAudioBufferMillisecondsLength(capture, 10);
        Utils.SetUseEventSync(capture, true);
        logger.ZLogInformation($"{capture.WaveFormat.ToString()}");
        
        capture.DataAvailable += (s, a) =>
        {
            // logger.ZLogDebug($"Received {a.BytesRecorded}");
            bufferedWaveProvider.AddSamples(a.Buffer, 0, a.BytesRecorded);
            // WinmmCallback(0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        };
        
        bufferedWaveProvider = new BufferedWaveProvider(capture.WaveFormat)
        {
            BufferDuration = TimeSpan.FromMilliseconds(bufferDuration), // 缓冲
            DiscardOnBufferOverflow = true          // 防止内存爆炸
        };
        
        resampler = new WdlResamplingSampleProvider(bufferedWaveProvider.ToSampleProvider(),3000);
        sample16 = resampler.ToWaveProvider16();
        
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
        Utils.ViGEmError(error);
        
        vigem_ds = ViGEmClient.vigem_target_ds5_alloc();
        error = ViGEmClient.vigem_target_add(vigem_client,vigem_ds);
        Utils.ViGEmError(error);
    }
    
    static void WinmmCallback(uint id, uint msg, IntPtr user, IntPtr dw1, IntPtr dw2)
    {
        if (latency.ElapsedTicks > (10.666 + 3) * TimeSpan.TicksPerMillisecond)
        {
            logger.ZLogWarning($"Warning: lag detected. {latency.ElapsedMilliseconds} ms");
            // Thread.Sleep(TimeSpan.FromTicks((long)10.666 * TimeSpan.TicksPerMillisecond - latency.ElapsedTicks));
        }
        latency.Restart();
        
        byte[] buffer = new byte[512];
        sample16.Read(buffer, 0, 512);
        if (buffer.All(b => b != 0))
        {
            return;
        }
    
        byte[] data = new byte[report33 ? 206 : 142]; // 0x33:206 0x32:142
        // 似乎与ReportId无关，主要靠的是packetId，尝试把0x32换成0x35同样可以工作
        data[0] = (byte)(report33 ? 0x33 : 0x32); // 感觉 0x32 和 0x33 差不多，不知道为什么它要用33
        data[1] = (byte)(reportSeqCounter << 4);
        reportSeqCounter = (byte)((reportSeqCounter + 1) & 0x0F);
        // Packet 0x11
        data[2] = 0x11 | 0 << 6 | 1 << 7; // pid(0x11) unk(false) sized(true)
        data[3] = 7;
        data[4] = 0b11111110;
        if (!report33)
        {
            data[5] = 0;
            data[6] = 0;
            data[7] = 0;
            data[8] = 0;
            data[9] = 0xFF;
        }
        else
        {
            // 来自DSX的神秘参数？
            data[5] = 0x40;
            data[6] = 0x40;
            data[7] = 0x40;
            data[8] = 0x40;
            data[9] = 0x40;
        }
        data[10] = packetCounter++;
        // Packet 0x12
        data[11] = 0x12 | 0 << 6 | 1 << 7;
        data[12] = (byte)SAMPLE_SIZE;
        for (int i = 13,offset = 0; i < SAMPLE_SIZE + 13; i += 2,offset += 8)
        {
            var ch3 = (short)((buffer[offset + 5] << 8) | (buffer[offset + 4] & 0xFF));
            var ch4 = (short)((buffer[offset + 7] << 8) | (buffer[offset + 6] & 0xFF));
            // BiQuadFilter.LowPassFilter(ch3, 1500, 0.7f);
            // BiQuadFilter.LowPassFilter(ch4, 1500, 0.7f);
            // Console.WriteLine($"{i - 13} {BitConverter.ToString(buffer)}");
            data[i] = (byte)Math.Clamp(((ch3 >> 8)) * GAIN,-128,127);
            data[i + 1] = (byte)Math.Clamp(((ch4 >> 8)) * GAIN,-128,127);
        }
        /*data[77] = 0x90;
        data[78] = 0x3f;
        Array.Copy(stateData, 0, data, 79, stateData.Length);*/
        
    
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
                await Task.Delay(5);
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
                    outputData[1] = (byte)(outputSeq << 4 + 2);
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

    static void Run(bool verbose,bool report33,bool disableViGEm,int bufferDuration,bool useWinmm)
    {
        InitLogger(verbose);
        logger.ZLogInformation($"Volume Gain: {GAIN} report33: {report33} disableViGEm: {disableViGEm} bufferDuration: {bufferDuration}");
        // Connect To BT Dualsense
        InitHid(0x054C, 0x0CE6);
        if (!disableViGEm)
        { 
            InitViGEm();
        }
        InitAudioCapture(bufferDuration);
        latency = new Stopwatch();
        
        logger.ZLogInformation($"Hello World");
        winmm.timeBeginPeriod(1);
        capture.StartRecording();
        latency.Start();
        if (useWinmm)
        { 
            winmm.Start((uint)intervalMs + 1, WinmmCallback);
        }

        if (!disableViGEm)
        {
            Task inputTask = InputForwardTask();
            Task outputTask = OutputForwardTask();
        }

        var sw = new Stopwatch();
        if (!useWinmm)
        {
            sw.Start();
        }
        while (capture.CaptureState != CaptureState.Stopped)
        {
            if (useWinmm)
            {
                Thread.Sleep(500);
                continue;
            }
            
            if (sw.ElapsedTicks <= intervalNs / TimeSpan.NanosecondsPerTick)
            {
                continue;
            }
            sw.Restart();
            WinmmCallback(0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public static int Main(string[] args)
    {
        var rootCommand = new RootCommand("A tool that forwards PS5 DualSense haptics audio over Bluetooth.");

        var logLevelOption = new Option<bool>("--verbose")
        {
            Description = "Default: false",
            DefaultValueFactory = parseResult => false,
        };
        var reportId = new Option<bool>("--33")
        {
            Description = "Using 0x33 ReportId",
            DefaultValueFactory = parseResult => false,
        };
        var gain = new Option<float>("--gain")
        {
            Description = "Volume Gain",
            DefaultValueFactory = parseResult => 2.0f
        };
        var disableViGEm = new Option<bool>("--disable-vigem")
        {
            Description = "Disable ViGEm (No Virtual Controller Created)",
            DefaultValueFactory = parseResult => false
        };
        var bufferDuration = new Option<int>("--buffer")
        {
            Description = "Buffer Duration (ms)",
            DefaultValueFactory = parseResult => 64
        };
        var winmm = new Option<bool>("--winmm")
        {
            Description = "使用 winmm 进行定时，资源占用低，但是声音会有点小卡顿",
            DefaultValueFactory = parseResult => false
        };
        rootCommand.Options.Add(logLevelOption);
        rootCommand.Options.Add(reportId);
        rootCommand.Options.Add(gain);
        rootCommand.Options.Add(disableViGEm);
        rootCommand.Options.Add(bufferDuration);
        rootCommand.Options.Add(winmm);
        rootCommand.SetAction(parseResult =>
        {
            GAIN = parseResult.GetValue(gain);
            Run(
                verbose: parseResult.GetValue(logLevelOption),
                report33: parseResult.GetValue(reportId),
                disableViGEm: parseResult.GetValue(disableViGEm),
                bufferDuration: parseResult.GetValue(bufferDuration),
                useWinmm:parseResult.GetValue(winmm)
                );
        });
        return rootCommand.Parse(args).Invoke();
    }
}



