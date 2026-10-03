using System.Diagnostics;
using System.Text;

namespace ProjectHub.Api.Modules.Whiteboard;

/// <summary>The engine process died or did not answer; the input that was being processed is treated as poison.</summary>
public sealed class WhiteboardEngineException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Runs all Yjs decoding in a child process (ADR 0009). yrs aborts the process on some malformed updates,
/// so a bad update may only ever take down this helper, never the API. The child is the API assembly itself,
/// started with <see cref="WhiteboardEngineHost.Argument"/>; it is restarted on the next call after a crash.
/// One process per API instance; calls are serialized.
/// </summary>
public sealed class WhiteboardEngine(ILogger<WhiteboardEngine> logger) : IDisposable
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? process;

    public async Task<bool> IsValidUpdateAsync(byte[] update, CancellationToken ct)
    {
        var response = await CallAsync(WhiteboardEngineHost.Validate, [update], ct);
        return response[0] == 1;
    }

    /// <summary>Merges a snapshot (may be null) and later updates into one update holding the whole document.</summary>
    public async Task<byte[]> MergeAsync(byte[]? snapshot, IReadOnlyList<byte[]> updates, CancellationToken ct) =>
        await CallAsync(WhiteboardEngineHost.Merge, [snapshot ?? [], .. updates], ct);

    public async Task<IReadOnlyList<TaskCard>> ReadTaskCardsAsync(byte[] state, CancellationToken ct) =>
        WhiteboardEngineHost.DecodeTaskCards(await CallAsync(WhiteboardEngineHost.TaskCards, [state], ct));

    public void Dispose()
    {
        Stop();
        gate.Dispose();
    }

    private async Task<byte[]> CallAsync(byte operation, IReadOnlyList<byte[]> payloads, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var child = process ??= Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CallTimeout);
            try
            {
                var input = child.StandardInput.BaseStream;
                await WhiteboardEngineHost.WriteFrameAsync(input, operation, payloads, timeout.Token);
                await input.FlushAsync(timeout.Token);

                var output = child.StandardOutput.BaseStream;
                var status = await WhiteboardEngineHost.ReadExactlyAsync(output, 1, timeout.Token);
                var length = BitConverter.ToInt32(await WhiteboardEngineHost.ReadExactlyAsync(output, 4, timeout.Token));
                var body = await WhiteboardEngineHost.ReadExactlyAsync(output, length, timeout.Token);
                if (status[0] != WhiteboardEngineHost.Ok)
                {
                    throw new WhiteboardEngineException(Encoding.UTF8.GetString(body));
                }

                return body;
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException or OperationCanceledException or InvalidOperationException)
            {
                // A crash, a hang or a broken pipe: the process state is unknown, so it is replaced.
                Stop();
                ct.ThrowIfCancellationRequested();
                throw new WhiteboardEngineException("The whiteboard engine stopped while processing the input.", ex);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private Process Start()
    {
        var api = typeof(WhiteboardEngine).Assembly.Location;
        var current = Environment.ProcessPath ?? "dotnet";
        var runsAsDotnet = Path.GetFileNameWithoutExtension(current).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        var runsAsApi = Path.GetFileNameWithoutExtension(current) == Path.GetFileNameWithoutExtension(api);

        var start = new ProcessStartInfo
        {
            // Published API (apphost) starts itself; otherwise the dotnet host runs the API assembly.
            FileName = runsAsApi ? current : runsAsDotnet ? current : Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (!runsAsApi)
        {
            start.ArgumentList.Add(api);
        }

        start.ArgumentList.Add(WhiteboardEngineHost.Argument);
        var child = Process.Start(start) ?? throw new WhiteboardEngineException("The whiteboard engine could not be started.");
        child.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                logger.LogWarning("Whiteboard engine: {Line}", e.Data);
            }
        };
        child.BeginErrorReadLine();
        logger.LogInformation("Whiteboard engine started (process {ProcessId})", child.Id);
        return child;
    }

    private void Stop()
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        process.Dispose();
        process = null;
    }
}

/// <summary>
/// The child side of <see cref="WhiteboardEngine"/>: reads framed requests from stdin and answers on stdout.
/// Frame: operation (1 byte), payload count (int32), then each payload as length (int32) and bytes.
/// Answer: status (1 byte), length (int32), body.
/// </summary>
public static class WhiteboardEngineHost
{
    public const string Argument = "--whiteboard-engine";

    public const byte Validate = 1;
    public const byte Merge = 2;
    public const byte TaskCards = 3;

    public const byte Ok = 0;
    public const byte Failed = 1;

    public static void Run()
    {
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        while (true)
        {
            var header = new byte[5];
            if (input.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                return;
            }

            var payloads = new byte[BitConverter.ToInt32(header, 1)][];
            for (var i = 0; i < payloads.Length; i++)
            {
                var length = new byte[4];
                input.ReadExactly(length);
                payloads[i] = new byte[BitConverter.ToInt32(length)];
                input.ReadExactly(payloads[i]);
            }

            byte status = Ok;
            byte[] body;
            try
            {
                body = header[0] switch
                {
                    Validate => [WhiteboardDocuments.IsValidUpdate(payloads[0]) ? (byte)1 : (byte)0],
                    Merge => WhiteboardDocuments.Merge(payloads[0].Length == 0 ? null : payloads[0], payloads.Skip(1)),
                    TaskCards => EncodeTaskCards(WhiteboardDocuments.ReadTaskCards(payloads[0])),
                    _ => throw new InvalidOperationException($"Unknown operation {header[0]}."),
                };
            }
            catch (Exception ex)
            {
                status = Failed;
                body = Encoding.UTF8.GetBytes(ex.Message);
            }

            output.WriteByte(status);
            output.Write(BitConverter.GetBytes(body.Length));
            output.Write(body);
            output.Flush();
        }
    }

    internal static async Task WriteFrameAsync(Stream stream, byte operation, IReadOnlyList<byte[]> payloads, CancellationToken ct)
    {
        var header = new byte[5];
        header[0] = operation;
        BitConverter.TryWriteBytes(header.AsSpan(1), payloads.Count);
        await stream.WriteAsync(header, ct);
        foreach (var payload in payloads)
        {
            await stream.WriteAsync(BitConverter.GetBytes(payload.Length), ct);
            await stream.WriteAsync(payload, ct);
        }
    }

    internal static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, ct);
        return buffer;
    }

    internal static IReadOnlyList<TaskCard> DecodeTaskCards(byte[] body)
    {
        using var reader = new BinaryReader(new MemoryStream(body), Encoding.UTF8);
        var cards = new List<TaskCard>();
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            cards.Add(new TaskCard(reader.ReadString(), new Guid(reader.ReadBytes(16))));
        }

        return cards;
    }

    private static byte[] EncodeTaskCards(IReadOnlyList<TaskCard> cards)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            writer.Write(cards.Count);
            foreach (var card in cards)
            {
                writer.Write(card.ObjectId);
                writer.Write(card.TaskId.ToByteArray());
            }
        }

        return stream.ToArray();
    }
}
