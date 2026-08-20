using System.Collections.Concurrent;
using DatabaseBackupRestore.Models;

namespace DatabaseBackupRestore.Services;
public class BackupHistoryStore
{
    private readonly ConcurrentQueue<BackupHistory> _entries = new();
    private int _nextId = 1;
    public event Action<BackupHistory>? OnEntryAdded;
    public BackupHistory Add(BackupHistory entry)
    {
        entry.Id = Interlocked.Increment(ref _nextId);
        _entries.Enqueue(entry);
        OnEntryAdded?.Invoke(entry);
        return entry;
    }
    public IReadOnlyList<BackupHistory> GetAll()
    {
        return _entries
            .OrderByDescending(e => e.Date)
            .ToList();
    }
    public void Clear() => _entries.Clear();
}