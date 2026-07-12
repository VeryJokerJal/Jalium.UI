using System.Collections;
using System.ComponentModel;
using System.Globalization;
#if JALIUM_UI_CORE
using ContractValueSerializer = Jalium.UI.Markup.ValueSerializer;
using ContractIValueSerializerContext = Jalium.UI.Markup.IValueSerializerContext;
#else
using ContractValueSerializer = Jalium.UI.Media.ValueSerializer;
using ContractIValueSerializerContext = Jalium.UI.Media.IValueSerializerContext;
#endif

namespace Jalium.UI.Media
{
    [TypeConverter(typeof(Int32CollectionConverter))]
    public sealed class Int32Collection : Freezable, IList<int>, ICollection<int>, IEnumerable<int>, IList, ICollection, IEnumerable, IFormattable
    {
        private readonly List<int> _items;
        private uint _version;

        public Int32Collection() => _items = [];
        public Int32Collection(int capacity) => _items = new List<int>(capacity);

        public Int32Collection(IEnumerable<int> collection)
        {
            ArgumentNullException.ThrowIfNull(collection);
            _items = new List<int>(collection);
        }

        public int this[int index]
        {
            get { ReadPreamble(); return _items[index]; }
            set
            {
                WritePreamble();
                _items[index] = value;
                _version++;
                WritePostscript();
            }
        }

        public int Count { get { ReadPreamble(); return _items.Count; } }
        bool ICollection<int>.IsReadOnly => IsFrozen;
        bool IList.IsReadOnly => IsFrozen;
        bool IList.IsFixedSize => IsFrozen;
        bool ICollection.IsSynchronized => IsFrozen;
        object ICollection.SyncRoot => this;

        public void Add(int value)
        {
            WritePreamble();
            _items.Add(value);
            _version++;
            WritePostscript();
        }

        public void Clear()
        {
            WritePreamble();
            _items.Clear();
            _version++;
            WritePostscript();
        }

        public bool Contains(int value) { ReadPreamble(); return _items.Contains(value); }
        public int IndexOf(int value) { ReadPreamble(); return _items.IndexOf(value); }

        public void Insert(int index, int value)
        {
            WritePreamble();
            _items.Insert(index, value);
            _version++;
            WritePostscript();
        }

        public bool Remove(int value)
        {
            WritePreamble();
            bool removed = _items.Remove(value);
            if (removed)
            {
                _version++;
                WritePostscript();
            }
            return removed;
        }

        public void RemoveAt(int index)
        {
            WritePreamble();
            _items.RemoveAt(index);
            _version++;
            WritePostscript();
        }

        public void CopyTo(int[] array, int index) { ReadPreamble(); _items.CopyTo(array, index); }
        public Enumerator GetEnumerator() { ReadPreamble(); return new Enumerator(this); }
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = Cast(value);
        }

        int IList.Add(object? value)
        {
            Add(Cast(value));
            return Count - 1;
        }

        bool IList.Contains(object? value) => value is int number && Contains(number);
        int IList.IndexOf(object? value) => value is int number ? IndexOf(number) : -1;
        void IList.Insert(int index, object? value) => Insert(index, Cast(value));
        void IList.Remove(object? value) { if (value is int number) Remove(number); }

        void ICollection.CopyTo(Array array, int index)
        {
            ReadPreamble();
            ArgumentNullException.ThrowIfNull(array);
            ((ICollection)_items).CopyTo(array, index);
        }

        public new Int32Collection Clone() => (Int32Collection)base.Clone();
        public new Int32Collection CloneCurrentValue() => (Int32Collection)base.CloneCurrentValue();
        protected override Freezable CreateInstanceCore() => new Int32Collection();

        protected override void CloneCore(Freezable source)
        {
            base.CloneCore(source);
            CopyFrom((Int32Collection)source);
        }

        protected override void CloneCurrentValueCore(Freezable source)
        {
            base.CloneCurrentValueCore(source);
            CopyFrom((Int32Collection)source);
        }

        protected override void GetAsFrozenCore(Freezable source)
        {
            base.GetAsFrozenCore(source);
            CopyFrom((Int32Collection)source);
        }

        protected override void GetCurrentValueAsFrozenCore(Freezable source)
        {
            base.GetCurrentValueAsFrozenCore(source);
            CopyFrom((Int32Collection)source);
        }

        private void CopyFrom(Int32Collection source)
        {
            _items.Clear();
            _items.AddRange(source._items);
            _version++;
        }

        public static Int32Collection Parse(string source)
        {
            ArgumentNullException.ThrowIfNull(source);
            string[] tokens = source.Split(
                [',', ';', ' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = new Int32Collection(tokens.Length);
            foreach (string token in tokens)
                result._items.Add(int.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture));
            return result;
        }

        public override string ToString() => ConvertToString(null, null);
        public string ToString(IFormatProvider? provider) => ConvertToString(null, provider);
        string IFormattable.ToString(string? format, IFormatProvider? provider) => ConvertToString(format, provider);

        private string ConvertToString(string? format, IFormatProvider? provider)
        {
            ReadPreamble();
            provider ??= CultureInfo.InvariantCulture;
            return string.Join(' ', _items.Select(value => value.ToString(format, provider)));
        }

        private static int Cast(object? value) => value is int number
            ? number
            : throw new ArgumentException($"Value must be of type {typeof(int)}.", nameof(value));

        public struct Enumerator : IEnumerator<int>, IEnumerator
        {
            private readonly Int32Collection _collection;
            private readonly uint _version;
            private int _index;
            private int _current;

            internal Enumerator(Int32Collection collection)
            {
                _collection = collection;
                _version = collection._version;
                _index = -1;
                _current = default;
            }

            public readonly int Current => _index >= 0 && _index < _collection._items.Count
                ? _current
                : throw new InvalidOperationException("The enumerator is not positioned on an element.");
            readonly object IEnumerator.Current => Current;
            public void Dispose() { }

            public bool MoveNext()
            {
                VerifyVersion();
                if (++_index < _collection._items.Count)
                {
                    _current = _collection._items[_index];
                    return true;
                }
                _index = _collection._items.Count;
                _current = default;
                return false;
            }

            void IEnumerator.Reset()
            {
                VerifyVersion();
                _index = -1;
                _current = default;
            }

            private readonly void VerifyVersion()
            {
                if (_version != _collection._version)
                    throw new InvalidOperationException("Collection was modified after the enumerator was created.");
            }
        }
    }

    public sealed class Int32CollectionConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            value is string text ? Int32Collection.Parse(text) : base.ConvertFrom(context, culture, value)!;

        public override object? ConvertTo(
            ITypeDescriptorContext? context,
            CultureInfo? culture,
            object? value,
            Type destinationType)
        {
            ArgumentNullException.ThrowIfNull(destinationType);
            return destinationType == typeof(string) && value is Int32Collection collection
                ? collection.ToString(culture)
                : base.ConvertTo(context, culture, value, destinationType);
        }
    }
}

namespace Jalium.UI.Media.Converters
{
    public class Int32CollectionValueSerializer : ContractValueSerializer
    {
        public override bool CanConvertFromString(string value, ContractIValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, ContractIValueSerializerContext? context) => value is Int32Collection;
        public override object ConvertFromString(string value, ContractIValueSerializerContext? context) => Int32Collection.Parse(value);
        public override string ConvertToString(object value, ContractIValueSerializerContext? context) =>
            value is Int32Collection collection
                ? collection.ToString(CultureInfo.InvariantCulture)
                : throw new ArgumentException("Value must be an Int32Collection.", nameof(value));
    }
}
