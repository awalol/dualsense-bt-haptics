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
}