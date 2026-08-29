using System;
using System.Diagnostics;
using GTA;
using GTA.Math;

internal enum JusticeMetric
{
    CrimeDetection,
    IncidentProcessing,
    Persistence
}

internal sealed class JusticeMetricAccumulator
{
    private const int Capacity = 128;
    private readonly long[] _samples = new long[Capacity];
    private int _count;
    private int _next;
    private long _maximum;
    private long _total;

    internal void RecordElapsedTicks(long elapsedTicks)
    {
        long safeTicks = Math.Max(0L, elapsedTicks);
        if (_count == Capacity)
        {
            _total -= _samples[_next];
        }
        else
        {
            _count++;
        }

        _samples[_next] = safeTicks;
        _next = (_next + 1) % Capacity;
        _total += safeTicks;
        _maximum = Math.Max(_maximum, safeTicks);
    }

    internal double AverageMilliseconds
    {
        get
        {
            return _count == 0
                ? 0.0
                : TicksToMilliseconds(_total / _count);
        }
    }

    internal double P95Milliseconds
    {
        get { return PercentileMilliseconds(95); }
    }

    internal double P99Milliseconds
    {
        get { return PercentileMilliseconds(99); }
    }

    internal double MaximumMilliseconds
    {
        get { return TicksToMilliseconds(_maximum); }
    }

    private double PercentileMilliseconds(int percentile)
    {
        if (_count == 0)
        {
            return 0.0;
        }

        long[] ordered = new long[_count];
        Array.Copy(_samples, ordered, _count);
        Array.Sort(ordered);
        int index = Math.Min(
            ordered.Length - 1,
            Math.Max(0, (int)Math.Ceiling(ordered.Length * percentile / 100.0) - 1));
        return TicksToMilliseconds(ordered[index]);
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }
}

internal sealed class JusticeWorldSnapshot
{
    internal static readonly Ped[] EmptyPeds = new Ped[0];
    internal static readonly Vehicle[] EmptyVehicles = new Vehicle[0];

    internal Ped[] NearbyPeds { get; set; } = EmptyPeds;

    internal Vehicle[] NearbyVehicles { get; set; } = EmptyVehicles;

    internal long CapturedAtMs { get; set; }

    internal int CenterHandle { get; set; }

    internal int PedQueryCount { get; set; }

    internal int VehicleQueryCount { get; set; }
}

internal static class JusticeSpatialMath
{
    internal static bool IsWithinSquaredDistance(
        Vector3 left,
        Vector3 right,
        float maximumDistance)
    {
        float safeMaximum = Math.Max(0.0f, maximumDistance);
        float dx = left.X - right.X;
        float dy = left.Y - right.Y;
        float dz = left.Z - right.Z;
        return dx * dx + dy * dy + dz * dz <= safeMaximum * safeMaximum;
    }
}

public sealed partial class DonJEnemySpawner
{
    private const float JusticeWorldSnapshotRadius = 160.0f;

    private readonly JusticeWorldSnapshot _justiceWorldSnapshot =
        new JusticeWorldSnapshot();
    private readonly JusticeMetricAccumulator _justiceCrimeDetectionMetrics =
        new JusticeMetricAccumulator();
    private readonly JusticeMetricAccumulator _justiceIncidentProcessingMetrics =
        new JusticeMetricAccumulator();
    private readonly JusticeMetricAccumulator _justicePersistenceMetrics =
        new JusticeMetricAccumulator();
    private int _justiceWorldPedQueries;
    private int _justiceWorldVehicleQueries;
    private int _justiceLastWorldEntityCount;

    private void CaptureJusticeWorldSnapshot(Ped player)
    {
        _justiceWorldSnapshot.NearbyPeds = JusticeWorldSnapshot.EmptyPeds;
        _justiceWorldSnapshot.NearbyVehicles = JusticeWorldSnapshot.EmptyVehicles;
        _justiceWorldSnapshot.CapturedAtMs = _justiceMonotonicTimeMs;
        _justiceWorldSnapshot.CenterHandle = 0;
        _justiceWorldSnapshot.PedQueryCount = 0;
        _justiceWorldSnapshot.VehicleQueryCount = 0;

        if (!Entity.Exists(player))
        {
            _justiceLastWorldEntityCount = 0;
            return;
        }

        _justiceWorldSnapshot.CenterHandle = player.Handle;
        _justiceWorldSnapshot.NearbyPeds =
            GetNearbyPedsSafe(player, JusticeWorldSnapshotRadius) ??
            JusticeWorldSnapshot.EmptyPeds;
        _justiceWorldSnapshot.PedQueryCount = 1;
        _justiceWorldPedQueries++;

        _justiceWorldSnapshot.NearbyVehicles =
            GetNearbyVehiclesSafe(player, JusticeWorldSnapshotRadius) ??
            JusticeWorldSnapshot.EmptyVehicles;
        _justiceWorldSnapshot.VehicleQueryCount = 1;
        _justiceWorldVehicleQueries++;
        _justiceLastWorldEntityCount =
            _justiceWorldSnapshot.NearbyPeds.Length +
            _justiceWorldSnapshot.NearbyVehicles.Length;
    }

    private Ped[] GetJusticeSnapshotPeds()
    {
        return _justiceWorldSnapshot.NearbyPeds ?? JusticeWorldSnapshot.EmptyPeds;
    }

    private Vehicle[] GetJusticeSnapshotVehicles()
    {
        return _justiceWorldSnapshot.NearbyVehicles ?? JusticeWorldSnapshot.EmptyVehicles;
    }

    private static bool IsJusticeSnapshotEntityWithin(
        Entity candidate,
        Entity center,
        float maximumDistance)
    {
        if (!Entity.Exists(candidate) || !Entity.Exists(center))
        {
            return false;
        }

        try
        {
            return JusticeSpatialMath.IsWithinSquaredDistance(
                candidate.Position,
                center.Position,
                maximumDistance);
        }
        catch
        {
            return false;
        }
    }

    private static long BeginJusticeMetric()
    {
        return Stopwatch.GetTimestamp();
    }

    private static void CompleteJusticeMetric(
        JusticeMetricAccumulator accumulator,
        long startedAt)
    {
        if (accumulator != null)
        {
            accumulator.RecordElapsedTicks(Stopwatch.GetTimestamp() - startedAt);
        }
    }
}
