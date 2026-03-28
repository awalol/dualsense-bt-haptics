using System.Buffers.Binary;
using System.Diagnostics;
using HidApi;
using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using NAudio.Wave;
using OpusSharp.Core;
using OpusSharp.Core.Extensions;

byte packetCounter = 0;
int reportSeqCounter = 0;
// var hid = new Device(0x054C, 0x0CE6);

var finder = new UsbDeviceFinder(0x054c, 0x0ce6);
var device = UsbDevice.OpenUsbDevice(finder);
if (device == null)
{
    Console.WriteLine("UsbDevice not found");
    return 1;
}

var writer = device.OpenEndpointWriter(WriteEndpointID.Ep05);

var SAMPLE_RATE = 48000;
var SAMPLE_SIZE = 200;
var CHANNELS = 2;
var BIT_DEPTH = 16;
var encoder = new OpusEncoder(SAMPLE_RATE, CHANNELS, OpusPredefinedValues.OPUS_APPLICATION_AUDIO);
encoder.SetExpertFrameDuration(OpusPredefinedValues.OPUS_FRAMESIZE_10_MS);
encoder.SetBitRate(SAMPLE_SIZE * 8 * 100);
encoder.SetVbr(false);
var audioFile = new AudioFileReader("test.wav"); // 48000Hz 16bit stereo

uint crc32(ReadOnlySpan<byte> data, int size)
{
    uint crc = ~0xEADA2D49;
    for (int i = 0; i < size; i++)
    {
        crc ^= data[i];
        for (int j = 0; j < 8; j++)
        {
            crc = ((crc >> 1) ^ (0xEDB88320 & (uint)-(crc & 1)));
        }
    }
    return ~crc;
}

var st = Stopwatch.StartNew();

void report0x35(float[] sample)
{
    byte[] data = new byte[204];
    // data[0] = 0x34;
    /*data[1] = (byte)(reportSeqCounter << 4);
    reportSeqCounter = (byte)((reportSeqCounter + 1) & 0x0F);
    
    // Packet 0x11
    data[2] = 0x11 | 0 << 6 | 1 << 7;
    data[3] = 7;
    data[4] = 0b11111110;
    data[5] = 0;
    data[6] = 0;
    data[7] = 0;
    data[8] = 0;
    data[9] = 0xFF;
    data[10] = packetCounter++;
    
    // Packet 0x16
    data[11] = 0x13 | 0 << 6 | 1 << 7; // Speaker: 0x13 Headset: 0x16
    data[12] = (byte) SAMPLE_SIZE; // 200 bytes*/
    
    byte[] encodedAudio = new byte[SAMPLE_SIZE];
    var encodedBytes = encoder.Encode(sample,
        SAMPLE_RATE / 100 * CHANNELS / 2, // 480 frames per 10ms (2ch: 2bytes per frame)
        encodedAudio, SAMPLE_SIZE);
    Array.Copy(encodedAudio, 0, data, 0, encodedBytes);

    var crc = crc32(data, data.Length - 4);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(data.Length - 4, 4), crc);
    int t;
    var num = writer.Write(data, 10, out t);
    // Console.WriteLine($"ErrorCode: {num} ,transfer: {t}");
}

var stopWatch = new Stopwatch();
stopWatch.Start();
while (audioFile.Position < audioFile.Length)
{
    if (stopWatch.ElapsedTicks <= 10.666 * TimeSpan.TicksPerMillisecond)
    {
        continue;
    }
    stopWatch.Restart();
    
    float[] audioData = new float[SAMPLE_RATE / 100 * CHANNELS]; // 960 bytes per 10ms
    
    int samplesRead = audioFile.Read(audioData, 0, audioData.Length);
    if (samplesRead == 0) continue;
    
    report0x35(audioData);
}

encoder.Dispose();
audioFile.Dispose();
device.Close();
return 0;