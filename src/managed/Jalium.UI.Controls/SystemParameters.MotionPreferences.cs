namespace Jalium.UI;

public static partial class SystemParameters
{
    private static readonly MotionPreferenceCache s_clientAreaAnimation =
        new(static () => GetSpiBool(SPI_GETCLIENTAREAANIMATION, true));
    private static readonly MotionPreferenceCache s_uiEffects =
        new(static () => GetSpiBool(SPI_GETUIEFFECTS, true));

    // Read on every hover/animation update. The settings message bridge
    // invalidates before notifying controls, so accessibility changes stay live.
    internal sealed class MotionPreferenceCache(Func<bool> read)
    {
        private readonly object _gate = new();
        private int _value = -1;

        internal bool Value
        {
            get
            {
                int value = Volatile.Read(ref _value);
                if (value >= 0) return value != 0;
                lock (_gate)
                {
                    if (_value < 0) Volatile.Write(ref _value, read() ? 1 : 0);
                    return _value != 0;
                }
            }
        }

        internal void Invalidate()
        {
            lock (_gate) Volatile.Write(ref _value, -1);
        }
    }
}
