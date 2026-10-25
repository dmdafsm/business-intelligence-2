using System.Security.Cryptography;

namespace BusinessIntelligence.Modules.Registry.Sources;

/// <summary>
/// Reads a local source file while hashing it. The size is checked before reading; the SHA-256 is checked after the
/// caller has consumed the stream, so a large file is read once and an import commits only when the hash matched.
/// </summary>
public sealed class VerifiedFileStream : Stream
{
    private readonly FileStream _inner;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly CatalogFile _expected;
    private long _read;

    private VerifiedFileStream(FileStream inner, CatalogFile expected)
    {
        _inner = inner;
        _expected = expected;
    }

    public static VerifiedFileStream Open(string path, CatalogFile expected)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        if (stream.Length != expected.Bytes)
        {
            stream.Dispose();
            throw new InvalidDataException($"{expected.FileName}: expected {expected.Bytes} bytes, found {stream.Length}.");
        }

        return new VerifiedFileStream(stream, expected);
    }

    public static async Task<byte[]> ReadAllAsync(string path, CatalogFile expected, CancellationToken cancellationToken)
    {
        await using var file = Open(path, expected);
        using var buffer = new MemoryStream((int)expected.Bytes);
        await file.CopyToAsync(buffer, cancellationToken);
        file.EnsureComplete();
        return buffer.ToArray();
    }

    /// <summary>Drains anything left unread and throws unless the whole file matched the catalog hash.</summary>
    public void EnsureComplete()
    {
        var buffer = new byte[1 << 16];
        while (Read(buffer, 0, buffer.Length) > 0)
        {
        }

        var actual = Convert.ToHexStringLower(_hash.GetHashAndReset());
        if (_read != _expected.Bytes || actual != _expected.Sha256)
        {
            throw new InvalidDataException($"{_expected.FileName}: SHA-256 {actual} does not match the catalog entry {_expected.Id}.");
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer);
        _hash.AppendData(buffer[..read]);
        _read += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        _hash.AppendData(buffer.Span[..read]);
        _read += read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _read;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }
}
