using System.Runtime.CompilerServices;
using LegacyBitmapImage = Jalium.UI.Media.BitmapImage;
using WriteableBitmap = Jalium.UI.Media.WriteableBitmap;

namespace Jalium.UI.Media.Imaging;

/// <summary>
/// 全局 LRU,把"大 source 小目标"的 BitmapImage 在 box-filter 后缓存成接近目标尺寸的缩略图。
/// 解决场景:像 Jalium.One CreateSolutionView 的 31 张 1.3MB PNG 显示成 168×190 / 72×72
/// —— GPU 上传 5MB RGBA 显示却只用 7K~32K 像素,resource 无法改时只能运行时压。
///
/// 关键决策:
///  - bucket 量化:next power-of-2,目标 72×72 → 128 bucket,168×190 → 256 bucket。
///    同 source 同 bucket 共享一份缓存;微小缩放变化不会让 cache miss 风暴。
///  - 仅对 <see cref="BitmapImage"/> 生效,<see cref="WriteableBitmap"/> 等动态像素源不参与
///    (每帧内容会变,反向 mip 反而拖后腿)。这一条通过 <c>is BitmapImage</c> 自然过滤,
///    因为 <c>WriteableBitmap</c> 继承自 <c>BitmapSource</c> 而不是 <c>BitmapImage</c>。
///  - 当 source 面积 ≤ target 面积 × 4 时不进 cache,直接用原图(收益不够,额外内存浪费)。
///  - 容量 100 MB,LRU 末端淘汰;`Dispose` 已被淘汰的 thumb。
///  - **合成异步化**:box-filter 在 <see cref="System.Threading.ThreadPool"/> 上做,UI 线程
///    永远只走 cache 查询。Cache miss 时调用方拿到原图 fallback、同时入队一份合成请求;
///    合成完成后下一帧 cache hit。 快速滚动期间 N 张 1.3MB 图同时 miss,UI 线程不再被
///    串行 box-filter 卡死。 同 (source,bucket) 的并发请求会去重 —— 已入队等待的不会
///    重复入队,合成中的不会被取消。
/// </summary>
internal static class BitmapDownscaleCache
{
    private sealed class Entry
    {
        public ulong Key;
        public LegacyBitmapImage Thumbnail = null!;
        public long Bytes;
    }

    private static readonly LinkedList<Entry> s_lru = new();
    private static readonly Dictionary<ulong, LinkedListNode<Entry>> s_byKey = new();
    // Set of (source,bucket) keys currently scheduled or being processed.
    // Guards against the same key being queued twice during a fast scroll
    // burst — the second DrawImage call hits this set and returns instead
    // of stacking another box-filter onto the thread pool.
    private static readonly HashSet<ulong> s_inFlight = new();
    private static long s_totalBytes;
    private const long MaxBytes = 100L * 1024 * 1024;  // 100 MB managed
    private const int MaxBucketDim = 4096;

    // Hard cap on outstanding async requests. A fast-scroll burst with 100+
    // distinct images would otherwise queue every single one onto the pool,
    // each waiting for the previous to finish — the user scrolls past them
    // before the work ever lands. Dropping excess requests is harmless:
    // the next frame that still needs that thumb re-enqueues it.
    private const int MaxInFlight = 8;

    // 触发阈值:source 面积必须至少是 target 的 4×,否则 box-filter 的代价 > 上传收益
    private const int MinPixelAreaRatio = 4;

    private static readonly object s_lock = new();

    /// <summary>
    /// 在缓存中查找接近 (targetW, targetH) 的 source 缩略图。命中时把 thumb 写到 out 参数
    /// 并返回 true;未命中(包括 source 太小、动态、解码失败)返回 false。 **永不在 UI 线程
    /// 合成**,合成请求通过 <see cref="TryGetOrCreate"/> 调用方在 fallback 同时入队的
    /// 异步路径完成。
    /// </summary>
    public static bool TryGetOrCreate(LegacyBitmapImage source, int targetW, int targetH, out LegacyBitmapImage thumb)
    {
        thumb = null!;
        if (source.RawPixelData == null) return false;

        int srcW = source.PixelWidth;
        int srcH = source.PixelHeight;
        if (srcW <= 0 || srcH <= 0 || targetW <= 0 || targetH <= 0) return false;

        long srcPixels = (long)srcW * srcH;
        long tgtPixels = (long)targetW * targetH;
        if (srcPixels <= tgtPixels * MinPixelAreaRatio) return false;

        int bucketW = NextPow2Bucket(targetW);
        int bucketH = NextPow2Bucket(targetH);
        if (bucketW >= srcW && bucketH >= srcH) return false;  // 不算缩略

        ulong key = MakeKey(source, bucketW, bucketH);

        lock (s_lock)
        {
            if (s_byKey.TryGetValue(key, out var node))
            {
                // LRU touch + return.
                s_lru.Remove(node);
                s_lru.AddFirst(node);
                thumb = node.Value.Thumbnail;
                return true;
            }
        }

        // Miss: schedule async synthesis (no-op if already in flight or queue
        // is at capacity). Caller will fall back to the original source for
        // this frame; the next frame that needs the same (source, bucket)
        // either hits cache (synthesis done) or re-fallback (still pending).
        EnqueueSynthesis(source, srcW, srcH, bucketW, bucketH, key);
        return false;
    }

    private static ulong MakeKey(LegacyBitmapImage source, int bucketW, int bucketH)
    {
        // identity hash (32 bit) × bucketW (16 bit) × bucketH (16 bit)
        // identity hash 碰撞概率极低;碰撞最坏 cache miss 一次,无 crash。
        int sourceId = RuntimeHelpers.GetHashCode(source);
        return ((ulong)(uint)sourceId << 32)
             | ((ulong)(ushort)bucketW << 16)
             | (ulong)(ushort)bucketH;
    }

    private static void EnqueueSynthesis(LegacyBitmapImage source, int srcW, int srcH, int bucketW, int bucketH, ulong key)
    {
        lock (s_lock)
        {
            if (s_inFlight.Count >= MaxInFlight) return;
            if (!s_inFlight.Add(key)) return;  // already scheduled
        }

        // Capture only what we need; the source BitmapImage's RawPixelData is
        // immutable for static BitmapImage (the only kind we cache), so reading
        // it from the worker thread is safe.
        System.Threading.ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            var (src, srcWLocal, srcHLocal, bw, bh, k) = state;
            try
            {
                var rawPixels = src.RawPixelData;
                if (rawPixels == null) return;
                int stride = src.PixelStride > 0 ? src.PixelStride : srcWLocal * 4;

                byte[] thumbPixels = BoxFilterDownscale(rawPixels, srcWLocal, srcHLocal, stride, bw, bh);
                var newThumb = LegacyBitmapImage.FromPixels(thumbPixels, bw, bh, bw * 4);
                if (newThumb == null) return;

                long newBytes = (long)bw * bh * 4;
                int evictedCount = 0;

                lock (s_lock)
                {
                    if (s_byKey.TryGetValue(k, out var existing))
                    {
                        // Another worker raced us — keep the existing one.
                        s_lru.Remove(existing);
                        s_lru.AddFirst(existing);
                        newThumb.Dispose();
                        return;
                    }

                    var entry = new Entry { Key = k, Thumbnail = newThumb, Bytes = newBytes };
                    var newNode = new LinkedListNode<Entry>(entry);
                    s_lru.AddFirst(newNode);
                    s_byKey[k] = newNode;
                    s_totalBytes += newBytes;

                    while (s_totalBytes > MaxBytes && s_lru.Last != null && s_lru.Last != newNode)
                    {
                        var tail = s_lru.Last;
                        s_byKey.Remove(tail.Value.Key);
                        s_totalBytes -= tail.Value.Bytes;
                        tail.Value.Thumbnail.Dispose();
                        s_lru.RemoveLast();
                        ++evictedCount;
                    }
                }

                if (evictedCount > 0)
                {
                    try { Diagnostics.RenderDiagnostics.OnBitmapDownscaleEviction(evictedCount); }
                    catch { /* diag disabled */ }
                }
            }
            finally
            {
                lock (s_lock) s_inFlight.Remove(k);
            }
        }, (source, srcW, srcH, bucketW, bucketH, key), preferLocal: false);
    }

    /// <summary>
    /// 清空缓存(配合 idle-resource reclaimer)。释放所有 thumb 资源。
    /// </summary>
    public static void Clear()
    {
        lock (s_lock)
        {
            foreach (var node in s_lru)
            {
                node.Thumbnail.Dispose();
            }
            s_lru.Clear();
            s_byKey.Clear();
            s_totalBytes = 0;
        }
    }

    private static int NextPow2Bucket(int n)
    {
        if (n <= 1) return 1;
        int p = 1;
        while (p < n) p <<= 1;
        return Math.Min(p, MaxBucketDim);
    }

    /// <summary>
    /// Scalar 2D box-filter downscale (BGRA8 → BGRA8)。源是 packed 像素 + stride,目标是
    /// tight-packed bucketW × bucketH × 4。每个 dst 像素平均其覆盖的所有 src 像素。SIMD 优化
    /// 可后续叠加;scalar 版本一次性 box-filter 在 ms 级别(几十张 1.3MB 图 ~50ms),够用。
    /// </summary>
    internal static byte[] BoxFilterDownscale(
        byte[] src, int srcW, int srcH, int srcStride, int dstW, int dstH)
    {
        var dst = new byte[(long)dstW * dstH * 4];
        for (int dy = 0; dy < dstH; ++dy)
        {
            long sy0 = (long)dy * srcH / dstH;
            long sy1 = ((long)dy + 1) * srcH / dstH;
            if (sy1 <= sy0) sy1 = sy0 + 1;
            if (sy1 > srcH) sy1 = srcH;

            for (int dx = 0; dx < dstW; ++dx)
            {
                long sx0 = (long)dx * srcW / dstW;
                long sx1 = ((long)dx + 1) * srcW / dstW;
                if (sx1 <= sx0) sx1 = sx0 + 1;
                if (sx1 > srcW) sx1 = srcW;

                long b = 0, g = 0, r = 0, a = 0, count = 0;
                for (long yy = sy0; yy < sy1; ++yy)
                {
                    long rowOff = yy * srcStride;
                    for (long xx = sx0; xx < sx1; ++xx)
                    {
                        long o = rowOff + xx * 4;
                        b += src[o];
                        g += src[o + 1];
                        r += src[o + 2];
                        a += src[o + 3];
                        ++count;
                    }
                }
                long doff = ((long)dy * dstW + dx) * 4;
                if (count == 0) count = 1;
                dst[doff]     = (byte)(b / count);
                dst[doff + 1] = (byte)(g / count);
                dst[doff + 2] = (byte)(r / count);
                dst[doff + 3] = (byte)(a / count);
            }
        }
        return dst;
    }

    /// <summary>当前缓存总字节(thumb 像素)。</summary>
    public static long TotalBytes
    {
        get { lock (s_lock) return s_totalBytes; }
    }

    /// <summary>当前缓存条目数。</summary>
    public static int EntryCount
    {
        get { lock (s_lock) return s_byKey.Count; }
    }
}
