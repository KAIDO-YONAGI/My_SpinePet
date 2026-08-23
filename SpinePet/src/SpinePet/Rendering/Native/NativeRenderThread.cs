using System.Windows.Threading;

namespace SpinePet.Rendering.Native;

internal sealed class NativeRenderThread : IDisposable
{
    private readonly Thread _thread;
    private readonly TaskCompletionSource<NativeCharacterRenderEngine> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Dispatcher? _dispatcher;
    private NativeCharacterRenderEngine? _engine;
    private int _closed;

    public NativeRenderThread(Action<NativeCharacterRenderEngine> attach)
    {
        _thread = new Thread(() => Run(attach))
        {
            IsBackground = true,
            Name = "SpinePet Native Renderer"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _engine = _ready.Task.GetAwaiter().GetResult();
    }

    public int ManagedThreadId => _thread.ManagedThreadId;

    public Task InvokeAsync(Action<NativeCharacterRenderEngine> action) =>
        InvokeAsync(engine =>
        {
            action(engine);
            return true;
        });

    public Task<T> InvokeAsync<T>(
        Func<NativeCharacterRenderEngine, T> action)
    {
        if (Volatile.Read(ref _closed) != 0)
            return Task.FromCanceled<T>(new CancellationToken(true));

        Dispatcher dispatcher = _dispatcher ??
            throw new InvalidOperationException("Render dispatcher is unavailable.");
        NativeCharacterRenderEngine engine = _engine ??
            throw new InvalidOperationException("Render engine is unavailable.");
        return dispatcher.InvokeAsync(
            () => action(engine),
            DispatcherPriority.Normal).Task;
    }

    public Task InvokeAsync(
        Func<NativeCharacterRenderEngine, Task> action)
    {
        if (Volatile.Read(ref _closed) != 0)
            return Task.FromCanceled(new CancellationToken(true));

        Dispatcher dispatcher = _dispatcher ??
            throw new InvalidOperationException("Render dispatcher is unavailable.");
        NativeCharacterRenderEngine engine = _engine ??
            throw new InvalidOperationException("Render engine is unavailable.");
        return dispatcher.InvokeAsync(
            () => action(engine),
            DispatcherPriority.Normal).Task.Unwrap();
    }

    public Task<T> InvokeAsync<T>(
        Func<NativeCharacterRenderEngine, Task<T>> action)
    {
        if (Volatile.Read(ref _closed) != 0)
            return Task.FromCanceled<T>(new CancellationToken(true));

        Dispatcher dispatcher = _dispatcher ??
            throw new InvalidOperationException("Render dispatcher is unavailable.");
        NativeCharacterRenderEngine engine = _engine ??
            throw new InvalidOperationException("Render engine is unavailable.");
        return dispatcher.InvokeAsync(
            () => action(engine),
            DispatcherPriority.Normal).Task.Unwrap();
    }

    public void Post(Action<NativeCharacterRenderEngine> action)
    {
        if (Volatile.Read(ref _closed) != 0)
            return;

        Dispatcher? dispatcher = _dispatcher;
        NativeCharacterRenderEngine? engine = _engine;
        if (dispatcher == null || engine == null)
            return;

        try
        {
            _ = dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(() => action(engine)));
        }
        catch (InvalidOperationException)
        {
            // Dispatcher shutdown won the race with this command.
        }
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;

        Dispatcher? dispatcher = _dispatcher;
        NativeCharacterRenderEngine? engine = _engine;
        if (dispatcher != null && engine != null)
        {
            if (dispatcher.CheckAccess())
            {
                engine.Close();
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            }
            else
            {
                dispatcher.Invoke(() =>
                {
                    engine.Close();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                });
            }
        }

        if (Thread.CurrentThread != _thread)
            _thread.Join();
    }

    public void Dispose() => Close();

    private void Run(Action<NativeCharacterRenderEngine> attach)
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            NativeCharacterRenderEngine engine =
                new(_dispatcher);
            _engine = engine;
            attach(engine);
            _ready.TrySetResult(engine);
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
        }
        finally
        {
            _engine?.Dispose();
            _engine = null;
            _dispatcher = null;
        }
    }
}
