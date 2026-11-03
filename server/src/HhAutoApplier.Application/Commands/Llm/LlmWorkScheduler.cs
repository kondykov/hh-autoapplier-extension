using HhAutoApplier.Domain.Applications;

namespace HhAutoApplier.Application.Commands.Llm;

public sealed class LlmWorkScheduler
{
    private readonly object _gate = new();
    private readonly HashSet<int> _reservedApplications = [];
    private Guid? _lastUserId;
    private bool _hasLastUser;

    public WorkReservation ReserveFair(IReadOnlyCollection<JobApplication> candidates, int limit)
    {
        if (limit <= 0 || candidates.Count == 0)
            return new WorkReservation(this, []);

        lock (_gate)
        {
            var owners = candidates
                .Where(application => !_reservedApplications.Contains(application.Id))
                .GroupBy(application => application.UserId)
                .OrderBy(owner => owner.Key)
                .Select(owner => new OwnerQueue(owner.Key, new Queue<JobApplication>(owner)))
                .ToList();

            if (owners.Count == 0)
                return new WorkReservation(this, []);

            var start = _hasLastUser
                ? owners.FindIndex(owner => Comparer<Guid?>.Default.Compare(owner.UserId, _lastUserId) > 0)
                : Random.Shared.Next(owners.Count);
            if (start < 0)
                start = 0;

            if (start > 0)
                owners = owners.Skip(start).Concat(owners.Take(start)).ToList();

            var batch = new List<JobApplication>(Math.Min(limit, candidates.Count));
            while (batch.Count < limit && owners.Count > 0)
            {
                for (var index = 0; index < owners.Count && batch.Count < limit;)
                {
                    var owner = owners[index];
                    if (!owner.Applications.TryDequeue(out var application))
                    {
                        owners.RemoveAt(index);
                        continue;
                    }

                    batch.Add(application);
                    _reservedApplications.Add(application.Id);
                    _lastUserId = owner.UserId;
                    _hasLastUser = true;
                    index++;
                }
            }

            return new WorkReservation(this, batch);
        }
    }

    private void Release(IReadOnlyList<JobApplication> applications)
    {
        lock (_gate)
        {
            foreach (var application in applications)
                _reservedApplications.Remove(application.Id);
        }
    }

    private sealed record OwnerQueue(Guid? UserId, Queue<JobApplication> Applications);

    public sealed class WorkReservation(LlmWorkScheduler scheduler, IReadOnlyList<JobApplication> applications) : IDisposable
    {
        private int _disposed;

        public IReadOnlyList<JobApplication> Applications { get; } = applications;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                scheduler.Release(Applications);
        }
    }
}
