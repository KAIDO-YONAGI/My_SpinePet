using SpinePet.Infrastructure;

namespace SpinePet.Rendering.Native;

// Groups native window and graphics lifetime so the host cannot accidentally
// dispose the input window before detaching its callback.
internal sealed class NativeRenderSession : IDisposable
{
    public NativeCompositionWindow? Window { get; private set; }
    public NativeInputWindow? InputWindow { get; private set; }
    public NativeGraphicsDevice? Graphics { get; private set; }

    public bool IsInitialized => Window != null && Graphics != null;

    public Task Initialize(Action<uint, int, int> mouseInput)
    {
        if (IsInitialized)
            return Task.CompletedTask;

        Window = new NativeCompositionWindow();
        try
        {
            Graphics = new NativeGraphicsDevice(Window.Handle);
            InputWindow = new NativeInputWindow(
                Window.Left,
                Window.Top,
                Window.Width,
                Window.Height)
            {
                MouseInput = mouseInput
            };
            InputWindow.ClearAndHide();
            AppLogger.Write(
                nameof(NativeCharacterRenderHost),
                $"initialized render-window={Window.Handle} " +
                $"input-window={InputWindow.Handle} " +
                $"size={Window.Width}x{Window.Height} " +
                $"dpi={Window.DpiScale:F2}");
            return Task.CompletedTask;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Commit() => Graphics?.Commit();

    public void Dispose()
    {
        if (InputWindow != null)
            InputWindow.MouseInput = null;
        InputWindow?.Dispose();
        InputWindow = null;
        Graphics?.Dispose();
        Graphics = null;
        Window?.Dispose();
        Window = null;
    }
}
