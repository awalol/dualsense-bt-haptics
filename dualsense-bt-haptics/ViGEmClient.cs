using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace dualsense_bt_haptics;

using PVIGEM_CLIENT = IntPtr;
using PVIGEM_TARGET = IntPtr;

public sealed partial class ViGEmClient
{
    [DllImport("vigemclient.dll", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    internal static extern PVIGEM_CLIENT vigem_alloc();
    
    [DllImport("vigemclient.dll", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void vigem_free(
        PVIGEM_CLIENT vigem);
    
    [DllImport("vigemclient.dll", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    internal static extern VIGEM_ERROR vigem_connect(
        PVIGEM_CLIENT vigem);
    
    [DllImport("vigemclient.dll", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    internal static extern VIGEM_ERROR vigem_target_add(
        PVIGEM_CLIENT vigem,
        PVIGEM_TARGET target);
    
    [DllImport("vigemclient.dll", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    internal static extern PVIGEM_TARGET vigem_target_ds5_alloc();
    
    [DllImport("vigemclient.dll", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    internal static extern VIGEM_ERROR vigem_target_ds5_await_output_report_timeout(
        PVIGEM_CLIENT vigem,
        PVIGEM_TARGET target,
        UInt32 milliseconds,
        ref DS5_AWAIT_OUTPUT_BUFFER buffer);
    
    [DllImport("vigemclient.dll", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    internal static extern VIGEM_ERROR vigem_target_ds5_update(
        PVIGEM_CLIENT vigem,
        PVIGEM_TARGET target,
        DS5_REPORT report);
    
    internal enum VIGEM_ERROR : UInt32
    {
        VIGEM_ERROR_NONE = 0x20000000,
        VIGEM_ERROR_BUS_NOT_FOUND = 0xE0000001,
        VIGEM_ERROR_NO_FREE_SLOT = 0xE0000002,
        VIGEM_ERROR_INVALID_TARGET = 0xE0000003,
        VIGEM_ERROR_REMOVAL_FAILED = 0xE0000004,
        VIGEM_ERROR_ALREADY_CONNECTED = 0xE0000005,
        VIGEM_ERROR_TARGET_UNINITIALIZED = 0xE0000006,
        VIGEM_ERROR_TARGET_NOT_PLUGGED_IN = 0xE0000007,
        VIGEM_ERROR_BUS_VERSION_MISMATCH = 0xE0000008,
        VIGEM_ERROR_BUS_ACCESS_FAILED = 0xE0000009,
        VIGEM_ERROR_CALLBACK_ALREADY_REGISTERED = 0xE0000010,
        VIGEM_ERROR_CALLBACK_NOT_FOUND = 0xE0000011,
        VIGEM_ERROR_BUS_ALREADY_CONNECTED = 0xE0000012,
        VIGEM_ERROR_BUS_INVALID_HANDLE = 0xE0000013,
        VIGEM_ERROR_XUSB_USERINDEX_OUT_OF_RANGE = 0xE0000014,
        VIGEM_ERROR_INVALID_PARAMETER = 0xE0000015,
        VIGEM_ERROR_NOT_SUPPORTED = 0xE0000016,
        VIGEM_ERROR_WINAPI = 0xE0000017,
        VIGEM_ERROR_TIMED_OUT = 0xE0000018,
        VIGEM_ERROR_IS_DISPOSING = 0xE0000019,
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Global")]
    internal struct DS5_AWAIT_OUTPUT_BUFFER
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] Buffer;
    }
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct DS5_REPORT
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 63)]
        public byte[] Report;
    }
}