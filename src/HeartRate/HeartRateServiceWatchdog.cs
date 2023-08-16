using System;
using System.Diagnostics;
using System.Threading;

namespace HeartRate;

internal class HeartRateServiceWatchdog : IDisposable
{
    private readonly TimeSpan _timeout;
    private readonly IHeartRateService _service;
    private readonly ulong? _bluetoothAddress;
    private readonly Stopwatch _lastUpdateTimer = Stopwatch.StartNew();
    private readonly object _sync = new();
    private bool _isDisposed = false;

    public HeartRateServiceWatchdog(
        TimeSpan timeout,
        IHeartRateService service,
        ulong? bluetoothAddress)
    {
        _timeout = timeout;
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _bluetoothAddress = bluetoothAddress;
        _service.HeartRateUpdated += Service_HeartRateUpdated;
    }

    private void Service_HeartRateUpdated(HeartRateReading reading)
    {
        lock (_sync)
        {
            _lastUpdateTimer.Restart();
        }
    }

    private void WatchdogThread()
    {
        while (!_isDisposed && !_service.IsDisposed)
        {
            var needsRefresh = false;
            lock (_sync)
            {
                if (_isDisposed)
                {
                    return;
                }

                if (_lastUpdateTimer.Elapsed > _timeout)
                {
                    needsRefresh = true;
                }
            }

            if (needsRefresh)
            {
                DebugLog.WriteLog("Restarting services...");
                try
                {
                    _service.InitiateDefault(_bluetoothAddress);

                    lock (_sync)
                    {
                        _lastUpdateTimer.Restart();
                    }
                }
                catch (Exception e)
                {
                    DebugLog.WriteLog($"Failed restart: {e}");
                }
            }

            Thread.Sleep(10000);
        }

        DebugLog.WriteLog("Watchdog thread exiting.");
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _lastUpdateTimer.Restart();
            var thread = new Thread(WatchdogThread)
            {
                Name = GetType().Name,
                IsBackground = true
            };

            thread.Start();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _isDisposed = true;
        }
    }
}