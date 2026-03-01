using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

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

    public static void ViGEmError(ViGEmClient.VIGEM_ERROR error)
    {
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
            case ViGEmClient.VIGEM_ERROR.VIGEM_ERROR_BUS_ACCESS_FAILED:
                throw new Exception("VIGEM_BUS_ACCESS_FAILED");
            case ViGEmClient.VIGEM_ERROR.VIGEM_ERROR_BUS_VERSION_MISMATCH:
                throw new Exception("VIGEM_BUS_VERSION_MISMATCH");
            default:
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
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
    
    public static bool SetUseEventSync(object captureInstance, bool value)
    {
        var type = captureInstance.GetType();

        FieldInfo? field = null;
        while (type != null && field == null)
        {
            field = type.GetField("isUsingEventSync", BindingFlags.NonPublic | BindingFlags.Instance);
            type = type.BaseType;
        }

        if (field == null)
        {
            return false;
        }

        field.SetValue(captureInstance, value);

        return false;
    }
}