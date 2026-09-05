using System.Diagnostics;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class ScreenVideoPump(Func<CancellationToken, Task<ProcessHandle?>> startSegment) {
    public const int InstantSegmentLimit = 3;
    public const int SegmentSeconds = 180;
    private const int BufferSize = 64 * 1024;
    public static readonly TimeSpan InstantSegment = TimeSpan.FromSeconds(2);

    public static string ScreenrecordCommand(string size, int bitrate) =>
        $"screenrecord --output-format=h264 --size {size} --bit-rate {bitrate} --time-limit {SegmentSeconds} -";

    public async Task<string?> RunAsync(Stream output, CancellationToken ct) {
        int instant = 0;
        byte[] buffer = new byte[BufferSize];
        while (!ct.IsCancellationRequested) {
            ProcessHandle? segment;
            try {
                segment = await startSegment(ct);
            } catch (OperationCanceledException) {
                return null;
            }

            if (segment is null) return "the device connection cannot stream screenrecord output";

            long started = Stopwatch.GetTimestamp();
            int exit;
            string tail;
            try {
                await CopyAsync(segment.Stdout, output, buffer, ct);
                exit = await segment.Exited.WaitAsync(ct);
                tail = segment.StderrTail();
            } catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) {
                return null;
            } finally {
                await segment.DisposeAsync();
            }

            if (Stopwatch.GetElapsedTime(started) >= InstantSegment) {
                instant = 0;
                continue;
            }

            if (++instant >= InstantSegmentLimit) return FailureNote(exit, tail);
        }

        return null;
    }

    private static string FailureNote(int exit, string tail) {
        string reason = tail.Length > 0 ? tail : $"exit code {exit}";
        return $"screenrecord ended immediately {InstantSegmentLimit} times in a row: {reason}";
    }

    private static async Task CopyAsync(Stream source, Stream output, byte[] buffer, CancellationToken ct) {
        int n;
        while ((n = await source.ReadAsync(buffer, ct)) > 0) {
            await output.WriteAsync(buffer.AsMemory(0, n), ct);
            await output.FlushAsync(ct);
        }
    }
}
