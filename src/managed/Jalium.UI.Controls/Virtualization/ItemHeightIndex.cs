namespace Jalium.UI.Controls.Virtualization;

/// <summary>
/// Stores measured item heights and provides fast offset/index conversion.
/// </summary>
internal sealed class ItemHeightIndex
{
    private const int BlockSize = 128;
    private const float MinHeight = 1f;
    private const float MaxHeight = 4096f;

    private float[] _measuredHeights = [];
    private float[] _blockSums = [];
    private float[] _blockPrefixes = [0f];
    // Per-block count of still-unmeasured items. Lets an estimate change be applied to the
    // unmeasured items incrementally across blocks (O(blocks)) instead of rebuilding the whole
    // index (O(itemCount)). Maintained alongside _blockSums (rebuilt together, decremented when
    // an item becomes measured).
    private int[] _blockUnmeasured = [];
    private int _count;
    private float _estimatedHeight;
    private int _measuredCount;
    private double _measuredTotal;
    private double _totalHeight;

    public ItemHeightIndex(double estimatedHeight = 28d)
    {
        _estimatedHeight = CoerceHeight(estimatedHeight);
    }

    public int Count => _count;

    public double EstimatedHeight => _estimatedHeight;

    public double TotalHeight => _totalHeight;

    public void Reset(int count, double estimatedHeight)
    {
        _estimatedHeight = CoerceHeight(estimatedHeight);
        _count = Math.Max(0, count);
        _measuredHeights = _count > 0 ? new float[_count] : [];
        _measuredCount = 0;
        _measuredTotal = 0;
        RebuildBlockSums();
    }

    public void EnsureCount(int count)
    {
        count = Math.Max(0, count);
        if (count == _count)
        {
            return;
        }

        if (_count == 0)
        {
            Reset(count, _estimatedHeight);
            return;
        }

        var newHeights = new float[count];
        var copyCount = Math.Min(_count, count);
        if (copyCount > 0)
        {
            Array.Copy(_measuredHeights, newHeights, copyCount);
        }

        _measuredHeights = newHeights;
        _count = count;
        RebuildBlockSums();
    }

    public void InsertRange(int index, int count)
    {
        if (count <= 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _count);
        var newHeights = new float[_count + count];
        if (index > 0)
        {
            Array.Copy(_measuredHeights, 0, newHeights, 0, index);
        }

        if (index < _count)
        {
            Array.Copy(_measuredHeights, index, newHeights, index + count, _count - index);
        }

        _measuredHeights = newHeights;
        _count += count;
        RebuildBlockSums();
    }

    public void RemoveRange(int index, int count)
    {
        if (_count == 0 || count <= 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _count - 1);
        count = Math.Min(count, _count - index);
        if (count <= 0)
        {
            return;
        }

        var newCount = _count - count;
        if (newCount == 0)
        {
            Reset(0, _estimatedHeight);
            return;
        }

        var newHeights = new float[newCount];
        if (index > 0)
        {
            Array.Copy(_measuredHeights, 0, newHeights, 0, index);
        }

        var tailCount = _count - (index + count);
        if (tailCount > 0)
        {
            Array.Copy(_measuredHeights, index + count, newHeights, index, tailCount);
        }

        _measuredHeights = newHeights;
        _count = newCount;
        RebuildBlockSums();
    }

    public void MoveRange(int oldIndex, int newIndex, int count)
    {
        if (count <= 0 || _count == 0)
        {
            return;
        }

        oldIndex = Math.Clamp(oldIndex, 0, _count - 1);
        count = Math.Min(count, _count - oldIndex);
        if (count <= 0)
        {
            return;
        }

        newIndex = Math.Clamp(newIndex, 0, _count - count);
        if (newIndex == oldIndex)
        {
            return;
        }

        var moved = new float[count];
        Array.Copy(_measuredHeights, oldIndex, moved, 0, count);

        if (oldIndex < newIndex)
        {
            Array.Copy(_measuredHeights, oldIndex + count, _measuredHeights, oldIndex, newIndex - oldIndex);
        }
        else
        {
            Array.Copy(_measuredHeights, newIndex, _measuredHeights, newIndex + count, oldIndex - newIndex);
        }

        Array.Copy(moved, 0, _measuredHeights, newIndex, count);
        RebuildBlockSums();
    }

    public void SetMeasuredHeight(int index, double height)
    {
        if (index < 0 || index >= _count)
        {
            return;
        }

        var newHeight = CoerceHeight(height);
        var oldMeasured = _measuredHeights[index];
        var wasUnmeasured = oldMeasured <= 0;
        var oldResolved = wasUnmeasured ? _estimatedHeight : oldMeasured;

        if (!wasUnmeasured)
        {
            _measuredTotal += newHeight - oldMeasured;
        }
        else
        {
            _measuredCount++;
            _measuredTotal += newHeight;
        }

        _measuredHeights[index] = newHeight;

        var block = index / BlockSize;
        if (block < 0 || block >= _blockSums.Length || _blockUnmeasured.Length != _blockSums.Length)
        {
            // Structure desynced (shouldn't happen) — rebuild defensively.
            RebuildBlockSums();
            return;
        }

        // This item just became measured: drop it from its block's unmeasured count so a
        // subsequent estimate change is no longer applied to it.
        if (wasUnmeasured && _blockUnmeasured[block] > 0)
        {
            _blockUnmeasured[block]--;
        }

        var candidate = _measuredCount > 0 ? (float)(_measuredTotal / _measuredCount) : _estimatedHeight;
        candidate = CoerceHeight(candidate);

        // When the running average drifts materially from the estimate, retarget it so unknown
        // items converge quickly — but apply the change to the still-unmeasured items
        // INCREMENTALLY across blocks (O(blocks)), instead of rebuilding the whole index from
        // _measuredHeights (O(itemCount)). The full rebuild was an O(itemCount) cliff that fired
        // repeatedly during convergence — pathological on huge lists (e.g. 1,000,000 rows),
        // showing up as multi-millisecond layout spikes while scrolling. Both this item's own
        // height delta and the estimate delta are folded into the block sums, then prefixes and
        // total are recomputed once (O(blocks)).
        if (_measuredCount >= 8 && Math.Abs(candidate - _estimatedHeight) > 0.5f)
        {
            var itemDelta = newHeight - oldResolved;
            if (itemDelta != 0f)
            {
                _blockSums[block] += itemDelta;
            }

            var estimateDelta = candidate - _estimatedHeight;
            _estimatedHeight = candidate;
            for (int b = 0; b < _blockSums.Length; b++)
            {
                var unmeasured = _blockUnmeasured[b];
                if (unmeasured > 0)
                {
                    _blockSums[b] += estimateDelta * unmeasured;
                }
            }

            float running = 0f;
            double total = 0d;
            for (int b = 0; b < _blockSums.Length; b++)
            {
                _blockPrefixes[b] = running;
                running += _blockSums[b];
                total += _blockSums[b];
            }

            _blockPrefixes[_blockSums.Length] = running;
            _totalHeight = total;
            return;
        }

        // No estimate change — propagate just this item's height delta.
        var delta = newHeight - oldResolved;
        if (Math.Abs(delta) <= double.Epsilon)
        {
            return;
        }

        _blockSums[block] += delta;
        for (int i = block + 1; i < _blockPrefixes.Length; i++)
        {
            _blockPrefixes[i] += delta;
        }

        _totalHeight += delta;
    }

    public double GetHeightAt(int index)
    {
        if (index < 0 || index >= _count)
        {
            return _estimatedHeight;
        }

        var measured = _measuredHeights[index];
        return measured > 0 ? measured : _estimatedHeight;
    }

    public double GetOffsetForIndex(int index)
    {
        if (_count == 0 || index <= 0)
        {
            return 0;
        }

        if (index >= _count)
        {
            return _totalHeight;
        }

        var block = index / BlockSize;
        var offset = _blockPrefixes[Math.Clamp(block, 0, _blockPrefixes.Length - 1)];
        var blockStart = block * BlockSize;
        for (int i = blockStart; i < index; i++)
        {
            var measured = _measuredHeights[i];
            offset += measured > 0 ? measured : _estimatedHeight;
        }

        return offset;
    }

    public int GetIndexAtOffset(double offset)
    {
        if (_count == 0)
        {
            return -1;
        }

        if (offset <= 0)
        {
            return 0;
        }

        if (offset >= _totalHeight)
        {
            return _count - 1;
        }

        int lo = 0;
        int hi = _blockSums.Length - 1;
        int block = 0;

        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var blockStart = _blockPrefixes[mid];
            var blockEnd = _blockPrefixes[mid + 1];
            if (offset < blockStart)
            {
                hi = mid - 1;
            }
            else if (offset >= blockEnd)
            {
                lo = mid + 1;
            }
            else
            {
                block = mid;
                break;
            }
        }

        var remaining = offset - _blockPrefixes[block];
        var start = block * BlockSize;
        var end = Math.Min(start + BlockSize, _count);
        for (int i = start; i < end; i++)
        {
            var h = _measuredHeights[i];
            var resolved = h > 0 ? h : _estimatedHeight;
            if (remaining < resolved)
            {
                return i;
            }

            remaining -= resolved;
        }

        return Math.Min(_count - 1, end);
    }

    private static float CoerceHeight(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 28f;
        }

        return (float)Math.Clamp(value, MinHeight, MaxHeight);
    }

    private void RebuildBlockSums()
    {
        if (_count == 0)
        {
            _blockSums = [];
            _blockPrefixes = [0f];
            _blockUnmeasured = [];
            _measuredCount = 0;
            _measuredTotal = 0;
            _totalHeight = 0;
            return;
        }

        var blockCount = (_count + BlockSize - 1) / BlockSize;
        _blockSums = new float[blockCount];
        _blockPrefixes = new float[blockCount + 1];
        _blockUnmeasured = new int[blockCount];

        _measuredCount = 0;
        _measuredTotal = 0;
        _totalHeight = 0;

        for (int i = 0; i < _count; i++)
        {
            var measured = _measuredHeights[i];
            var resolved = measured > 0 ? measured : _estimatedHeight;
            var block = i / BlockSize;
            if (measured > 0)
            {
                _measuredCount++;
                _measuredTotal += measured;
            }
            else
            {
                _blockUnmeasured[block]++;
            }

            _blockSums[block] += resolved;
            _totalHeight += resolved;
        }

        for (int i = 0; i < _blockSums.Length; i++)
        {
            _blockPrefixes[i + 1] = _blockPrefixes[i] + _blockSums[i];
        }
    }
}

