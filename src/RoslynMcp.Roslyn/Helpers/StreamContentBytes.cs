namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Reads through EOF into an exact result without copying a stable, seekable file.</summary>
internal static class StreamContentBytes
{
    internal static byte[] ReadAll(Stream stream)
    {
        var output = new Accumulator(stream);
        while (true)
        {
            var read = stream.Read(output.Writable.Span);
            if (read == 0) return output.Finish();
            output.Advance(read);
        }
    }

    internal static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var output = new Accumulator(stream);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(output.Writable, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (read == 0) return output.Finish();
            output.Advance(read);
        }
    }

    private sealed class Accumulator
    {
        private byte[] _buffer;
        private readonly byte[] _probe = new byte[1];
        private int _count;
        internal Accumulator(Stream stream)
        {
            var remaining = stream.CanSeek ? Math.Max(0, stream.Length - stream.Position) : 0;
            // Length is only a hint: oversized or shrinking streams may still produce a small result.
            if (remaining > Array.MaxLength) remaining = 0;
            _buffer = remaining == 0 ? [] : new byte[(int)remaining];
        }
        internal Memory<byte> Writable => _count == _buffer.Length ? _probe : _buffer.AsMemory(_count);
        internal void Advance(int read)
        {
            if (_count == _buffer.Length)
            {
                if (_count == Array.MaxLength) throw TooLarge();
                var capacity = (int)Math.Min(Array.MaxLength, Math.Max(4096L, (long)_count * 2));
                Array.Resize(ref _buffer, capacity);
                _buffer[_count++] = _probe[0];
            }
            else _count += read;
        }
        internal byte[] Finish()
        {
            if (_count != _buffer.Length) Array.Resize(ref _buffer, _count);
            return _buffer;
        }
        private static IOException TooLarge() => new("The complete stream cannot be represented by a byte array.");
    }
}
