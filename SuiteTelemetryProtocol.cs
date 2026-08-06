using System.IO.MemoryMappedFiles;

namespace BitsPleaseYT.SuiteIpc;

internal sealed record SuiteTelemetrySnapshot(
    bool Active,
    double InputLevel,
    double OutputLevel,
    double VoiceProbability,
    double Confidence,
    bool IsLearning,
    double[] BandLevels,
    double[] BandReductions);

internal sealed class SuiteTelemetryWriter : IDisposable
{
    private const int Capacity = 512;
    private const int Magic = 0x42505354;
    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;

    public SuiteTelemetryWriter(string module)
    {
        _map = MemoryMappedFile.CreateOrOpen(MapName(module), Capacity, MemoryMappedFileAccess.ReadWrite);
        _view = _map.CreateViewAccessor(0, Capacity, MemoryMappedFileAccess.ReadWrite);
    }

    public void Write(SuiteTelemetrySnapshot snapshot)
    {
        var bandCount = Math.Min(12, Math.Min(snapshot.BandLevels.Length, snapshot.BandReductions.Length));
        _view.Write(0, 0);
        _view.Write(4, snapshot.Active ? 1 : 0);
        _view.Write(8, bandCount);
        _view.Write(12, snapshot.IsLearning ? 1 : 0);
        _view.Write(16, snapshot.InputLevel);
        _view.Write(24, snapshot.OutputLevel);
        _view.Write(32, snapshot.VoiceProbability);
        _view.Write(40, snapshot.Confidence);
        for (var index = 0; index < bandCount; index++)
        {
            _view.Write(48 + index * 16, snapshot.BandLevels[index]);
            _view.Write(56 + index * 16, snapshot.BandReductions[index]);
        }
        Thread.MemoryBarrier();
        _view.Write(0, Magic);
    }

    public void Dispose()
    {
        _view.Dispose();
        _map.Dispose();
    }

    internal static string MapName(string module) => $"Local\\BitsPleaseYT.Suite.{module}.Telemetry";
}

internal sealed class SuiteTelemetryReader : IDisposable
{
    private const int Magic = 0x42505354;
    private readonly string _module;
    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;

    public SuiteTelemetryReader(string module) => _module = module;

    public bool TryRead(out SuiteTelemetrySnapshot snapshot)
    {
        snapshot = new SuiteTelemetrySnapshot(false, -60, -60, 0, 0, false, [], []);
        try
        {
            if (_view is null)
            {
                _map = MemoryMappedFile.OpenExisting(SuiteTelemetryWriter.MapName(_module), MemoryMappedFileRights.Read);
                _view = _map.CreateViewAccessor(0, 512, MemoryMappedFileAccess.Read);
            }
            if (_view.ReadInt32(0) != Magic)
            {
                return false;
            }
            var active = _view.ReadInt32(4) != 0;
            var bandCount = Math.Clamp(_view.ReadInt32(8), 0, 12);
            var isLearning = _view.ReadInt32(12) != 0;
            var inputLevel = _view.ReadDouble(16);
            var outputLevel = _view.ReadDouble(24);
            var voiceProbability = _view.ReadDouble(32);
            var confidence = _view.ReadDouble(40);
            var levels = new double[bandCount];
            var reductions = new double[bandCount];
            for (var index = 0; index < bandCount; index++)
            {
                levels[index] = _view.ReadDouble(48 + index * 16);
                reductions[index] = _view.ReadDouble(56 + index * 16);
            }
            Thread.MemoryBarrier();
            if (_view.ReadInt32(0) != Magic)
            {
                return false;
            }
            snapshot = new SuiteTelemetrySnapshot(active, inputLevel, outputLevel, voiceProbability, confidence, isLearning, levels, reductions);
            return true;
        }
        catch
        {
            _view?.Dispose();
            _map?.Dispose();
            _view = null;
            _map = null;
            return false;
        }
    }

    public void Dispose()
    {
        _view?.Dispose();
        _map?.Dispose();
    }
}