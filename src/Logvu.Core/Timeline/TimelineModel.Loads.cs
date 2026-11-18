using System.Runtime.ExceptionServices;
using Logvu.Core.Store;

namespace Logvu.Core.Timeline;

public sealed partial class TimelineModel
{
    /// <summary>The kinds of load, which also name the outstanding request slots.</summary>
    private enum LoadKind
    {
        /// <summary>The newest rows, which seed the window.</summary>
        Newest,

        /// <summary>Rows older than the window's first row, or older than the pivot when the window is being seeded.</summary>
        Older,

        /// <summary>Rows newer than the window's last row, or newer than the pivot when the window is being seeded.</summary>
        Newer,
    }

    /// <summary>One outstanding load. A result is applied only while its request is the current one for its kind.</summary>
    private sealed class LoadRequest
    {
        /// <summary>Creates a request.</summary>
        /// <param name="kind">The kind of load.</param>
        /// <param name="anchor">The row an older or newer load is relative to. Unused for a newest load.</param>
        /// <param name="generation">The window generation that issued the request.</param>
        /// <param name="count">The page size.</param>
        /// <param name="filter">The filter the page is read with.</param>
        /// <param name="inclusive">Whether a newer load starts with the anchor's own row.</param>
        public LoadRequest(LoadKind kind, RowKey anchor, int generation, int count, IRowFilter? filter, bool inclusive)
        {
            Kind = kind;
            Anchor = anchor;
            Generation = generation;
            Count = count;
            Filter = filter;
            Inclusive = inclusive;
        }

        /// <summary>The kind of load.</summary>
        public LoadKind Kind { get; }

        /// <summary>The row an older or newer load is relative to.</summary>
        public RowKey Anchor { get; }

        /// <summary>The window generation that issued the request.</summary>
        public int Generation { get; }

        /// <summary>The page size.</summary>
        public int Count { get; }

        /// <summary>The filter the page is read with.</summary>
        public IRowFilter? Filter { get; }

        /// <summary>Whether a newer load starts with the anchor's own row.</summary>
        public bool Inclusive { get; }

        /// <summary>Cancels when the request is superseded. The source may ignore it, and the result is then discarded.</summary>
        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>Cancels the request.</summary>
        public void Cancel() => Cancellation.Cancel();
    }

    private LoadRequest? _newest;
    private LoadRequest? _older;
    private LoadRequest? _newer;

    /// <summary>The outstanding request slot for a kind.</summary>
    private ref LoadRequest? Pending(LoadKind kind)
    {
        switch (kind)
        {
            case LoadKind.Newest:
                return ref _newest;
            case LoadKind.Older:
                return ref _older;
            default:
                return ref _newer;
        }
    }

    private static void Cancel(ref LoadRequest? request)
    {
        request?.Cancel();
        request = null;
    }

    /// <summary>Whether a request is still the outstanding one for its kind, in the current generation.</summary>
    private bool IsCurrent(LoadRequest request) =>
        request.Generation == _generation && ReferenceEquals(Pending(request.Kind), request);

    /// <summary>Starts a load and makes it the outstanding request for its kind.</summary>
    private void Request(LoadKind kind, RowKey anchor, bool inclusive = false)
    {
        var request = new LoadRequest(kind, anchor, _generation, PageHeights * _height, _filter, inclusive);
        Pending(kind) = request;
        Observe(request);
    }

    private Task<RowPage> Read(LoadRequest request)
    {
        CancellationToken ct = request.Cancellation.Token;
        return request.Kind switch
        {
            LoadKind.Newest => _source.ReadNewestAsync(request.Count, request.Filter, ct),
            LoadKind.Older => _source.ReadOlderAsync(request.Anchor, request.Count, request.Filter, ct),
            _ => _source.ReadNewerAsync(request.Anchor, request.Count, request.Filter, ct, request.Inclusive),
        };
    }

    /// <summary>
    /// Hands a request's read to the owner's thread when it completes. The read's continuation may run on the thread that completed
    /// the read, so it only posts; a superseded read's result is dropped there.
    /// </summary>
    private void Observe(LoadRequest request) =>
        Read(request).ContinueWith(task => Deliver(request, task), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private void Deliver(LoadRequest request, Task<RowPage> read)
    {
        if (read.IsCanceled)
        {
            return;
        }

        if (read.IsFaulted)
        {
            Exception ex = read.Exception!.GetBaseException();
            _post(() => Failed(request, ex));
            return;
        }

        RowPage page = read.Result;
        _post(() => Completed(request, page));
    }

    /// <summary>Applies a load's result, if its request is still current, and then settles the state.</summary>
    private void Completed(LoadRequest request, RowPage page)
    {
        if (!IsCurrent(request))
        {
            return;
        }

        Pending(request.Kind) = null;
        switch (request.Kind)
        {
            case LoadKind.Newest:
                PlaceNewest(page);
                break;
            case LoadKind.Older:
                ApplyOlder(request, page);
                break;
            default:
                ApplyNewer(request, page);
                break;
        }

        Finish();
    }

    /// <summary>
    /// Rethrows a read's unexpected failure on the owner's thread. The request is cleared first, so a later call can load again.
    /// </summary>
    private void Failed(LoadRequest request, Exception ex)
    {
        if (!IsCurrent(request))
        {
            return;
        }

        Pending(request.Kind) = null;
        ExceptionDispatchInfo.Capture(ex).Throw();
    }

    /// <summary>
    /// Prepends an older page. A page whose anchor is no longer the window's first row is stale, because rows were trimmed or reloaded
    /// since, and is dropped. Into an empty window, the page seeds it: a reload fills older rows that way.
    /// </summary>
    private void ApplyOlder(LoadRequest request, RowPage page)
    {
        if (_rows.Count == 0)
        {
            _rows.AddRange(page.Rows);
            _atStart = page.ReachedBoundary;
            _placed = true;
            return;
        }

        if (request.Anchor != _rows[0].Key)
        {
            return;
        }

        if (page.Rows.Count > 0)
        {
            _rows.InsertRange(0, page.Rows);
            _top += page.Rows.Count;
            _selected += page.Rows.Count;
        }

        if (page.ReachedBoundary)
        {
            _atStart = true;
        }
    }

    /// <summary>Appends a newer page. A page whose anchor is no longer the window's last row is stale and is dropped.</summary>
    private void ApplyNewer(LoadRequest request, RowPage page)
    {
        if (_rows.Count == 0)
        {
            SeedFromNewer(request, page);
            return;
        }

        if (request.Anchor != _rows[^1].Key)
        {
            return;
        }

        _rows.AddRange(page.Rows);
        if (page.ReachedBoundary)
        {
            _atEnd = true;
        }
    }

    /// <summary>
    /// Seeds an empty window from a reload's newer page, which starts at the pivot when the pivot's row still matches. When no row is at or
    /// after the pivot, the window is seeded from the older side instead, since the reads' anchors are exclusive.
    /// </summary>
    private void SeedFromNewer(LoadRequest request, RowPage page)
    {
        _rows.AddRange(page.Rows);
        _atEnd = page.ReachedBoundary;
        if (page.Rows.Count > 0 || _pivot is null)
        {
            _placed = true;
        }
        else
        {
            Request(LoadKind.Older, _pivot.Value);
        }
    }

    /// <summary>
    /// Trims the rows beyond the trim distance from each viewport edge, which clears that history edge. A trimmed edge drops its pending
    /// load, since that load's anchor is no longer the window's edge.
    /// </summary>
    private void Trim()
    {
        int limit = TrimHeights * _height;
        if (_top > limit)
        {
            int excess = _top - limit;
            _rows.RemoveRange(0, excess);
            _top -= excess;
            _selected -= excess;
            _atStart = false;
            Cancel(ref _older);
        }

        int beyondBottom = _rows.Count - (_top + _height);
        if (beyondBottom > limit)
        {
            _rows.RemoveRange(_top + _height + limit, beyondBottom - limit);
            _atEnd = false;
            Cancel(ref _newer);
        }
    }

    /// <summary>
    /// Requests the rows beyond a viewport edge when fewer than the margin are loaded there, unless the window reaches that edge of
    /// history or a request is already outstanding for it.
    /// </summary>
    private void EnsureMargin()
    {
        if (!_placed || _rows.Count == 0)
        {
            return;
        }

        int margin = MarginHeights * _height;
        if (!_atStart && _top < margin && _older is null)
        {
            Request(LoadKind.Older, _rows[0].Key);
        }

        if (!_atEnd && _rows.Count - (_top + _height) < margin && _newer is null)
        {
            Request(LoadKind.Newer, _rows[^1].Key);
        }
    }

    /// <summary>
    /// Runs queued commands in order while each one can be applied. A command that needs an unloaded row stays at the head of the queue,
    /// and the margin requests its rows.
    /// </summary>
    private void Drain()
    {
        while (_placed && _pivot is null && _queue.Count > 0 && TryApply(_queue.Peek()) )
        {
            _queue.Dequeue();
            Refresh();
        }
    }
}
