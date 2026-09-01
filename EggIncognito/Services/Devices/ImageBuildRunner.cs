using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class ImageBuildRunner(
    IServiceScopeFactory scopeFactory,
    ILogger<ImageBuildRunner> logger) {
    private int _running;

    public bool IsRunning => Volatile.Read(ref _running) != 0;

    public async Task<ImageBuildStartResult> StartAsync(ImageBuildSpec spec, CancellationToken ct) {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return new ImageBuildStartResult(false, null, spec.ResolvedTag, "another image build is already running");

        long id;
        try {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<ImageBuildStore>();
            id = await store.CreateAsync(spec, ct);
        } catch {
            Interlocked.Exchange(ref _running, 0);
            throw;
        }

        _ = Task.Run(() => RunDetachedAsync(id, spec), CancellationToken.None);
        return new ImageBuildStartResult(true, id, spec.ResolvedTag, null);
    }

    private async Task RunDetachedAsync(long id, ImageBuildSpec spec) {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var store = sp.GetRequiredService<ImageBuildStore>();
        try {
            var builder = sp.GetRequiredService<ImageBuilder>();
            var outcome = await builder.BuildAsync(spec, id, CancellationToken.None);
            logger.LogInformation("image build {Id} for {Tag} finished ok={Ok}: {Note}",
                id, outcome.Tag, outcome.Ok, outcome.Note ?? "");
        } catch (Exception ex) {
            logger.LogError(ex, "image build {Id} for {Tag} threw", id, spec.ResolvedTag);
            await store.FinishAsync(id, ImageBuildStates.Failed, spec.ResolvedTag, ex.Message, CancellationToken.None);
        } finally {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}
