using System.Runtime.InteropServices;
using MultiSessionHost.Desktop.Interfaces;

namespace MultiSessionHost.Desktop.Commands;

public sealed class WindowsScreenTravelInputDriver : IScreenTravelInputDriver
{
    private const uint InputMouse = 0;
    private const uint MouseEventfLeftDown = 0x0002;
    private const uint MouseEventfLeftUp = 0x0004;

    public Task<bool> ClickAsync(int x, int y, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!SetCursorPos(x, y))
        {
            return Task.FromResult(false);
        }

        var inputSize = Marshal.SizeOf<INPUT>();
        var inputs = new[]
        {
            new INPUT
            {
                Type = InputMouse,
                Data = new InputData
                {
                    Mouse = new MOUSEINPUT { Dx = 0, Dy = 0, MouseData = 0, Flags = MouseEventfLeftDown, Time = 0, ExtraInfo = IntPtr.Zero }
                }
            },
            new INPUT
            {
                Type = InputMouse,
                Data = new InputData
                {
                    Mouse = new MOUSEINPUT { Dx = 0, Dy = 0, MouseData = 0, Flags = MouseEventfLeftUp, Time = 0, ExtraInfo = IntPtr.Zero }
                }
            }
        };

        return Task.FromResult(SendInput((uint)inputs.Length, inputs, inputSize) == inputs.Length);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInputStructure);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)]
        public MOUSEINPUT Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}
