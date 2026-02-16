using System.Runtime.InteropServices;

public class winmm
{
    // 定义回调委托
    public delegate void TimerCallback(uint id, uint msg, IntPtr user, IntPtr dw1, IntPtr dw2);

    [DllImport("winmm.dll")]
    private static extern uint timeSetEvent(uint delay, uint resolution, TimerCallback callback, IntPtr user, uint eventType);

    [DllImport("winmm.dll")]
    private static extern uint timeKillEvent(uint id);

    private const uint TIME_PERIODIC = 1; // 周期性触发
    private static uint _timerId;

    public static void Start(uint intervalMs,TimerCallback callback)
    {
        // 参数：间隔, 精度, 回调函数, 用户数据, 事件类型
        _timerId = timeSetEvent(intervalMs, 0, callback, IntPtr.Zero, TIME_PERIODIC);
    }

    public static void Stop()
    {
        if (_timerId != 0) timeKillEvent(_timerId);
    }
}