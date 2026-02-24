using System.Reflection;

namespace dualsense_bt_haptics;

public class Utils
{
    public static uint crc32(ReadOnlySpan<byte> data, int size)
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
    
    public static bool SetAudioBufferMillisecondsLength(object captureInstance, int value)
    {
        var type = captureInstance.GetType();

        FieldInfo? field = null;
        while (type != null && field == null)
        {
            field = type.GetField("audioBufferMillisecondsLength", BindingFlags.NonPublic | BindingFlags.Instance);
            type = type.BaseType;
        }

        if (field == null)
        {
            return false;
        }

        field.SetValue(captureInstance, value);

        return true;
    }
}