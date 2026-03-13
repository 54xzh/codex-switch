// 窗口最小尺寸约束：通过 Win32 消息 WM_GETMINMAXINFO 限制窗口不可缩小到指定尺寸以下。
// 该逻辑仅影响窗口拖拽缩放，不影响程序主动 Resize 到更大尺寸。
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace codex_switch_winui.Services;

public static class WindowMinSize
{
    private const int GwlWndProc = -4;
    private const int WmGetMinMaxInfo = 0x0024;

    private static readonly ConcurrentDictionary<nint, Hook> Hooks = new();

    public static void SetMinSize(nint hwnd, int minWidth, int minHeight)
    {
        if (hwnd == nint.Zero)
        {
            return;
        }

        if (minWidth <= 0 || minHeight <= 0)
        {
            return;
        }

        Hooks.AddOrUpdate(
            hwnd,
            _ => Hook.Install(hwnd, minWidth, minHeight),
            (_, existing) =>
            {
                existing.MinWidth = minWidth;
                existing.MinHeight = minHeight;
                return existing;
            });
    }

    private sealed class Hook
    {
        public int MinWidth;
        public int MinHeight;

        private readonly nint _hwnd;
        private readonly WndProc _proc;
        private readonly nint _procPtr;
        private readonly nint _originalProc;

        private Hook(nint hwnd, int minWidth, int minHeight)
        {
            _hwnd = hwnd;
            MinWidth = minWidth;
            MinHeight = minHeight;

            _proc = WndProcImpl;
            _procPtr = Marshal.GetFunctionPointerForDelegate(_proc);
            _originalProc = SetWindowLongPtr(_hwnd, GwlWndProc, _procPtr);
        }

        public static Hook Install(nint hwnd, int minWidth, int minHeight) => new(hwnd, minWidth, minHeight);

        private nint WndProcImpl(nint hwnd, uint msg, nint wParam, nint lParam)
        {
            if (msg == WmGetMinMaxInfo)
            {
                try
                {
                    var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                    info.PtMinTrackSize.X = MinWidth;
                    info.PtMinTrackSize.Y = MinHeight;
                    Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
                }
                catch
                {
                    // ignore
                }
            }

            return CallWindowProc(_originalProc, hwnd, msg, wParam, lParam);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point PtReserved;
        public Point PtMaxSize;
        public Point PtMaxPosition;
        public Point PtMinTrackSize;
        public Point PtMaxTrackSize;
    }

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hwnd, uint msg, nint wParam, nint lParam);

    private static nint SetWindowLongPtr(nint hwnd, int index, nint newValue)
    {
        return nint.Size == 8 ? SetWindowLongPtr64(hwnd, index, newValue) : SetWindowLongPtr32(hwnd, index, newValue);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern nint SetWindowLongPtr32(nint hwnd, int index, nint newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr64(nint hwnd, int index, nint newValue);
}

