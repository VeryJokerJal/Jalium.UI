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
        var oldResolved = oldMeasured > 0 ? oldMeasured : _estimatedHeight;

        if (oldMeasured > 0)
        {
            _measuredTotal += newHeight - oldMeasured;
        }
        else
        {
            _measuredCount++;
            _measuredTotal += newHeight;
        }

        _measuredHeights[index] = newHeight;

        var candidate = _measuredCount > 0 ? (float)(_measuredTotal / _measuredCount) : _estimatedHeight;
        candidate = CoerceHeight(candidate);

        // Rebuild when estimate drift is material so unknown items converge quickly.
        if (_measuredCount >= 8 && Math.Abs(candidate - _estimatedHeight) > 0.5f)
        {
            _estimatedHeight = candidate;
            RebuildBlockSums();
            return;
        }

        var delta = newHeight - oldResolved;
        if (Math.Abs(delta) <= double.Epsilon)
        {
            return;
        }

        var block = index / BlockSize;
        if (block < 0 || block >= _blockSums.Length)
        {
            RebuildBlockSums();
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
            _measuredCount = 0;
            _measuredTotal = 0;
            _totalHeight = 0;
            return;
        }

        var blockCount = (_count + BlockSize - 1) / BlockSize;
        _blockSums = new float[blockCount];
        _blockPrefixes = new float[blockCount + 1];

        _measuredCount = 0;
        _measuredTotal = 0;
        _totalHeight = 0;

        for (int i = 0; i < _count; i++)
        {
            var measured = _measuredHeights[i];
            var resolved = measured > 0 ? measured : _estimatedHeight;
            if (measured > 0)
            {
                _measuredCount++;
                _measuredTotal += measured;
            }

            var block = i / BlockSize;
            _blockSums[block] += resolved;
            _totalHeight += resolved;
        }

        for (int i = 0; i < _blockSums.Length; i++)
        {
            _blockPrefixes[i + 1] = _blockPrefixes[i] + _blockSums[i];
        }
    }
}

