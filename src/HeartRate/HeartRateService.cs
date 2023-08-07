using System;
using System.IO;
using System.Linq;
using System.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace HeartRate;

internal enum ContactSensorStatus
{
    NotSupported,
    NotSupported2,
    NoContact,
    Contact
}

[Flags]
internal enum HeartRateFlags
{
    None = 0,
    IsShort = 1,
    HasEnergyExpended = 1 << 3,
    HasRRInterval = 1 << 4,
}

internal struct HeartRateReading
{
    public HeartRateFlags Flags { get; set; }
    public ContactSensorStatus Status { get; set; }
    public int BeatsPerMinute { get; set; }
    public int? EnergyExpended { get; set; }
    public int[] RRIntervals { get; set; }
    public bool IsError { get; set; }
    public string Error { get; set; }
}

internal interface IHeartRateService : IDisposable
{
    bool IsDisposed { get; }

    event HeartRateService.HeartRateUpdateEventHandler HeartRateUpdated;
    void InitiateDefault(ulong? BluetoothAddress);
    void Cleanup();
}

internal class HeartRateService : IHeartRateService
{
    // https://www.bluetooth.com/specifications/gatt/viewer?attributeXmlFile=org.bluetooth.characteristic.heart_rate_measurement.xml
    private const int _heartRateMeasurementCharacteristicId = 0x2A37;
    private static readonly Guid _heartRateMeasurementCharacteristicUuid =
        GattDeviceService.ConvertShortIdToUuid(_heartRateMeasurementCharacteristicId);

    public bool IsDisposed { get; private set; }

    private GattDeviceService _service;
    private byte[] _buffer;
    private readonly object _disposeSync = new();
    private readonly DebugLog _log = new(nameof(HeartRateService));

    public event HeartRateUpdateEventHandler HeartRateUpdated;
    public delegate void HeartRateUpdateEventHandler(HeartRateReading reading);

    public void InitiateDefault(ulong? bluetoothAddress)
    {
        while (true)
        {
            try
            {
                InitiateDefaultCore(bluetoothAddress);
                return; // success.
            }
            catch (Exception e)
            {
                _log.Write($"InitiateDefault exception: {e}");

                HeartRateUpdated?.Invoke(new HeartRateReading
                {
                    IsError = true,
                    Error = e.Message
                });
            }

            Thread.Sleep(TimeSpan.FromSeconds(2.5));
        }
    }

    private void InitiateDefaultCore(ulong? bluetoothAddress)
    {
        var deviceSelector = bluetoothAddress == null
            ? GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.HeartRate)
            : BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress(bluetoothAddress.Value);

        var devices = DeviceInformation
            .FindAllAsync(deviceSelector)
            .AsyncResult();

        if (bluetoothAddress == null)
        {
            // Scan for all devices, together with their addresses so that the user can add it to settings.xml later
            foreach (var device in devices)
            {
                var bluetoothDevice = BluetoothLEDevice.FromIdAsync(device.Id).AsyncResult();

                var properties = string.Join(",", device.Properties.Select(t => $"{t.Key}: {t.Value}"));
                _log.Write($"Found suitable device: [Name: {device.Name}, Address: {bluetoothDevice.BluetoothAddress}, Id: {device.Id}, IsEnabled: {device.IsEnabled}, properties: {properties}]");
            }
        }

        var foundDevice = devices.FirstOrDefault();

        if (foundDevice == null)
        {
            _log.Write("Unable to locate a device.");

            if(bluetoothAddress != null)
            {
                _log.Write($"There's a device with address {bluetoothAddress.Value} specified in settings, but this device can't be found.");
                _log.Write("Remove it from settings to try again and try to find another device.");
            }

            throw new ArgumentNullException(
                nameof(foundDevice),
                "Unable to locate heart rate device. Ensure it's connected and paired.");
        }

        var foundDeviceProperties = string.Join(",", foundDevice.Properties.Select(t => $"{t.Key}: {t.Value}"));
        _log.Write($"Trying to connect to device: [Name: {foundDevice.Name}, Id: {foundDevice.Id}, IsEnabled: {foundDevice.IsEnabled}, properties: {foundDeviceProperties}]");

        lock (_disposeSync)
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            Cleanup();

            GattDeviceService service;
            if (bluetoothAddress == null)
            {
                service = GattDeviceService
                    .FromIdAsync(foundDevice.Id)
                    .AsyncResult();
            }
            else
            {
                var bluetoothDevice = BluetoothLEDevice
                    .FromBluetoothAddressAsync(bluetoothAddress.Value)
                    .AsyncResult();

                if (bluetoothDevice == null)
                {
                    _log.Write("device null");
                    throw new ArgumentOutOfRangeException(
                        $"Unable to connect to device {foundDevice.Name} ({foundDevice.Id}). Is the device in use by another program? The Bluetooth adaptor may need to be turned off and on again.");
                }

                service = bluetoothDevice
                    .GetGattServicesForUuidAsync(GattServiceUuids.HeartRate)
                    .AsyncResult()
                    .Services
                    .FirstOrDefault();
            }

            Volatile.Write(ref _service, service);
        }

        if (_service == null)
        {
            _log.Write("service null");
            throw new ArgumentOutOfRangeException(
                $"Unable to get service to {foundDevice.Name} ({foundDevice.Id}). Is the device in use by another program? The Bluetooth adaptor may need to be turned off and on again.");
        }

        _log.Write($"Connected to device: [Name: {foundDevice.Name}, Address: {_service.Device.BluetoothAddress}]");

        var heartrate = _service
            .GetCharacteristicsForUuidAsync(_heartRateMeasurementCharacteristicUuid)
            .AsyncResult()
            .Characteristics
            .FirstOrDefault();

        if (heartrate == null)
        {
            throw new ArgumentOutOfRangeException(
                $"Unable to locate heart rate measurement on device {foundDevice.Name} ({foundDevice.Id}).");
        }

        _log.Write($"Service [CharacteristicProperties: {heartrate.CharacteristicProperties}, UserDescription: {heartrate.UserDescription}]");

        var status = heartrate
            .WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify)
            .AsyncResult();

        heartrate.ValueChanged += HeartRate_ValueChanged;

        DebugLog.WriteLog($"Started {status}");

        if (status != GattCommunicationStatus.Success)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status), status,
                "Attempt to configure service failed.");
        }
    }

    public void HeartRate_ValueChanged(
        GattCharacteristic sender,
        GattValueChangedEventArgs args)
    {
        var buffer = args.CharacteristicValue;
        if (buffer.Length == 0) return;

        var byteBuffer = Interlocked.Exchange(ref _buffer, null)
            ?? new byte[buffer.Length];

        if (byteBuffer.Length != buffer.Length)
        {
            byteBuffer = new byte[buffer.Length];
        }

        try
        {
            using var reader = DataReader.FromBuffer(buffer);
            reader.ReadBytes(byteBuffer);

            var readingValue = ReadBuffer(byteBuffer, (int)buffer.Length);

            if (readingValue == null)
            {
                DebugLog.WriteLog($"Buffer was too small. Got {buffer.Length}.");
                return;
            }

            HeartRateUpdated?.Invoke(readingValue.Value);
        }
        finally
        {
            Volatile.Write(ref _buffer, byteBuffer);
        }
    }

    internal static HeartRateReading? ReadBuffer(byte[] buffer, int length)
    {
        if (length == 0) return null;

        var ms = new MemoryStream(buffer, 0, length);
        var flags = (HeartRateFlags)ms.ReadByte();
        var isshort = flags.HasFlag(HeartRateFlags.IsShort);
        var contactSensor = (ContactSensorStatus)(((int)flags >> 1) & 3);
        var hasEnergyExpended = flags.HasFlag(HeartRateFlags.HasEnergyExpended);
        var hasRRInterval = flags.HasFlag(HeartRateFlags.HasRRInterval);
        var minLength = isshort ? 3 : 2;

        if (buffer.Length < minLength) return null;

        var reading = new HeartRateReading
        {
            Flags = flags,
            Status = contactSensor,
            BeatsPerMinute = isshort ? ms.ReadUInt16() : ms.ReadByte()
        };

        if (hasEnergyExpended)
        {
            reading.EnergyExpended = ms.ReadUInt16();
        }

        if (hasRRInterval)
        {
            var rrvalueCount = (buffer.Length - ms.Position) / sizeof(ushort);
            var rrvalues = new int[rrvalueCount];
            for (var i = 0; i < rrvalueCount; ++i)
            {
                rrvalues[i] = ms.ReadUInt16();
            }

            reading.RRIntervals = rrvalues;
        }

        return reading;
    }

    public void Cleanup()
    {
        var service = Interlocked.Exchange(ref _service, null);
        service.TryDispose();
    }

    public void Dispose()
    {
        lock (_disposeSync)
        {
            IsDisposed = true;

            Cleanup();
        }
    }
}