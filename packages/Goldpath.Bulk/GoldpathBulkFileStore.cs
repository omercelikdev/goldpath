using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Goldpath;

/// <summary>
/// The content-addressed file store (bulk RFC D1): bytes stream into chunked blob rows in
/// the app's own database, SHA-256 stamped as they flow — the 50 MB file never
/// materializes as one array. The hash is the dedup identity; the unique index makes the
/// race between two identical concurrent uploads lose gracefully.
/// </summary>
public sealed class GoldpathBulkFileStore<TContext>
    where TContext : DbContext
{
    /// <summary>Blob-row size: big enough to keep row counts sane, small enough to stream.</summary>
    internal const int ChunkBytes = 256 * 1024;

    private readonly TimeProvider _time;

    /// <summary>Creates the store.</summary>
    public GoldpathBulkFileStore(TimeProvider time) => _time = time;

    /// <summary>
    /// Persists the stream and returns the file row — the EXISTING row when identical
    /// bytes were stored before (the flag in the tuple tells which happened).
    /// </summary>
    public async Task<(GoldpathBulkFile File, bool Created)> SaveAsync(
        TContext db, Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var file = new GoldpathBulkFile
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            UploadedAt = _time.GetUtcNow(),
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[ChunkBytes];
        var index = 0;
        long total = 0;
        int filled;
        while ((filled = await FillAsync(stream, buffer, cancellationToken)) > 0)
        {
            sha.AppendData(buffer, 0, filled);
            total += filled;
            db.Set<GoldpathBulkFileChunk>().Add(new GoldpathBulkFileChunk
            {
                FileId = file.Id,
                Index = index++,
                Data = buffer[..filled],
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        file.Sha256 = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        file.Length = total;

        var existing = await db.Set<GoldpathBulkFile>().AsNoTracking()
            .FirstOrDefaultAsync(f => f.Sha256 == file.Sha256, cancellationToken);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return (existing, false);
        }

        db.Set<GoldpathBulkFile>().Add(file);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (file, true);
    }

    /// <summary>Opens a forward-only stream over the stored bytes (chunk rows load lazily).</summary>
    public Stream OpenRead(TContext db, Guid fileId) => new ChunkReadStream(db, fileId);

    /// <summary>Deletes the file's bytes and row (retention, D6). No-op when already gone.</summary>
    public async Task DeleteAsync(TContext db, Guid fileId, CancellationToken cancellationToken)
    {
        await db.Set<GoldpathBulkFileChunk>().Where(c => c.FileId == fileId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<GoldpathBulkFile>().Where(f => f.Id == fileId).ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<int> FillAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset;
    }

    private sealed class ChunkReadStream : Stream
    {
        private readonly TContext _db;
        private readonly Guid _fileId;
        private byte[] _current = [];
        private int _position;
        private int _nextIndex;
        private bool _finished;

        public ChunkReadStream(TContext db, Guid fileId)
        {
            _db = db;
            _fileId = fileId;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!EnsureChunk())
            {
                return 0;
            }

            var take = Math.Min(count, _current.Length - _position);
            Array.Copy(_current, _position, buffer, offset, take);
            _position += take;
            return take;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!await EnsureChunkAsync(cancellationToken))
            {
                return 0;
            }

            var take = Math.Min(buffer.Length, _current.Length - _position);
            _current.AsMemory(_position, take).CopyTo(buffer);
            _position += take;
            return take;
        }

        private bool EnsureChunk()
        {
            if (_position < _current.Length)
            {
                return true;
            }

            if (_finished)
            {
                return false;
            }

            var next = _db.Set<GoldpathBulkFileChunk>().AsNoTracking()
                .FirstOrDefault(c => c.FileId == _fileId && c.Index == _nextIndex);
            return Advance(next);
        }

        private async Task<bool> EnsureChunkAsync(CancellationToken cancellationToken)
        {
            if (_position < _current.Length)
            {
                return true;
            }

            if (_finished)
            {
                return false;
            }

            var index = _nextIndex;
            var next = await _db.Set<GoldpathBulkFileChunk>().AsNoTracking()
                .FirstOrDefaultAsync(c => c.FileId == _fileId && c.Index == index, cancellationToken);
            return Advance(next);
        }

        private bool Advance(GoldpathBulkFileChunk? next)
        {
            if (next is null)
            {
                _finished = true;
                return false;
            }

            _current = next.Data;
            _position = 0;
            _nextIndex = next.Index + 1;
            return true;
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override string ToString()
            => string.Create(CultureInfo.InvariantCulture, $"goldpath-bulk-file:{_fileId}");
    }
}
