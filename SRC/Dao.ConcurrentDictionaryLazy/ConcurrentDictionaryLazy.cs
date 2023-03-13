using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Dao.ConcurrentDictionaryLazy
{
    [Serializable]
    public class ConcurrentDictionaryLazy<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IDictionary
    {
        #region dictionary

        protected readonly ConcurrentDictionary<TKey, Lazy<TValue>> dictionary;

        IDictionary AsDictionary => this.dictionary;
        ICollection AsCollection => this.dictionary;
        IDictionary<TKey, Lazy<TValue>> AsDictionaryKeyValue => this.dictionary;
        ICollection<KeyValuePair<TKey, Lazy<TValue>>> AsCollectionKeyValue => this.dictionary;

        #endregion

        #region Constructor

        static readonly int processCount = Environment.ProcessorCount;
        static readonly IEqualityComparer<TKey> defaultComparer = EqualityComparer<TKey>.Default;
        readonly IEqualityComparer<TKey> comparer;

        public ConcurrentDictionaryLazy()
            : this(null) { }

        public ConcurrentDictionaryLazy(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer = null)
            : this(0, collection, comparer) { }

        public ConcurrentDictionaryLazy(IEqualityComparer<TKey> comparer)
            : this(0, comparer: comparer) { }

        public ConcurrentDictionaryLazy(int concurrencyLevel, IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer = null) =>
            this.dictionary = new ConcurrentDictionary<TKey, Lazy<TValue>>(concurrencyLevel <= 0 ? processCount : concurrencyLevel, ConvertToLazy(collection), this.comparer = comparer ?? defaultComparer);

        public ConcurrentDictionaryLazy(int concurrencyLevel, int capacity = 31, IEqualityComparer<TKey> comparer = null) =>
            this.dictionary = new ConcurrentDictionary<TKey, Lazy<TValue>>(concurrencyLevel <= 0 ? processCount : concurrencyLevel, capacity, this.comparer = comparer ?? defaultComparer);

        static IEnumerable<KeyValuePair<TKey, Lazy<TValue>>> ConvertToLazy(IEnumerable<KeyValuePair<TKey, TValue>> collection) =>
            collection.Select(s => new KeyValuePair<TKey, Lazy<TValue>>(s.Key, LazyValue(s.Value)));

        #endregion

        #region LazyValue

        static Lazy<TValue> LazyValue(TValue value) => new Lazy<TValue>(() => value);

        Lazy<TValue> LazyValue(TKey key, Func<TKey, TValue> valueFactory) => new Lazy<TValue>(() =>
        {
            try
            {
                return valueFactory(key);
            }
            catch (Exception)
            {
                Remove(key);
                throw;
            }
        });

        Func<TKey, Lazy<TValue>> LazyValue(Func<TKey, TValue> valueFactory) => k => LazyValue(k, valueFactory);

        Lazy<TValue> LazyValue(TKey key, Lazy<TValue> comparisonValue, Func<TKey, TValue, TValue> updateValueFactory) => new Lazy<TValue>(() =>
        {
            try
            {
                return updateValueFactory(key, comparisonValue.Value);
            }
            catch (Exception)
            {
                Remove(key);
                throw;
            }
        });

        Func<TKey, Lazy<TValue>, Lazy<TValue>> LazyValue(Func<TKey, TValue, TValue> updateValueFactory) =>
            (k, comparisonValue) => LazyValue(k, comparisonValue, updateValueFactory);

        #endregion

        #region Interfaces

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #region IDictionary<TKey, TValue>

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => ((IDictionary<TKey, TValue>)this).Add(item.Key, item.Value);

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) =>
            TryGetValue(item.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);

        void CopyTo<T>(T[] array, int index, Func<TKey, TValue, T> convertor)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            var tempArray = new object[array.Length];

            AsCollection.CopyTo(tempArray, index);

            for (var i = index; i < tempArray.Length; i++)
            {
                var current = array[i];
                if (!(current is KeyValuePair<TKey, Lazy<TValue>> kv))
                    continue;

                array[i] = convertor(kv.Key, kv.Value.Value);
            }
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) =>
            CopyTo(array, arrayIndex, (k, v) => new KeyValuePair<TKey, TValue>(k, v));

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) => Remove(item.Key);

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => AsCollectionKeyValue.IsReadOnly;

        void IDictionary<TKey, TValue>.Add(TKey key, TValue value) => AsDictionaryKeyValue.Add(key, LazyValue(value));

        #endregion

        #region IReadOnlyDictionary<TKey, TValue>

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

        #endregion

        #region IDictionary

        void IDictionary.Add(object key, object value) => AsDictionary.Add(key, LazyValue((TValue)value));

        bool IDictionary.Contains(object key) => AsDictionary.Contains(key);

        IDictionaryEnumerator IDictionary.GetEnumerator() => new DictionaryEnumerator(this);

        void IDictionary.Remove(object key) => AsDictionary.Remove(key);

        bool IDictionary.IsFixedSize => AsDictionary.IsFixedSize;
        bool IDictionary.IsReadOnly => AsDictionary.IsReadOnly;
        object IDictionary.this[object key]
        {
            get => ((Lazy<TValue>)AsDictionary[key]).Value;
            set => AsDictionary[key] = LazyValue((TValue)value);
        }
        ICollection IDictionary.Keys => (ICollection)Keys;
        ICollection IDictionary.Values => (ICollection)Values;

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            switch (array)
            {
                case KeyValuePair<TKey, TValue>[] target:
                    CopyTo(target, index, (k, v) => new KeyValuePair<TKey, TValue>(k, v));
                    break;

                case DictionaryEntry[] target:
                    CopyTo(target, index, (k, v) => new DictionaryEntry(k, v));
                    break;

                case object[] target:
                    CopyTo(target, index, (k, v) => v);
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        bool ICollection.IsSynchronized => AsCollection.IsSynchronized;
        object ICollection.SyncRoot => AsCollection.SyncRoot;

        #endregion

        #endregion

        #region DictionaryEnumerator

        class DictionaryEnumerator : IDictionaryEnumerator
        {
            readonly IEnumerator<KeyValuePair<TKey, TValue>> enumerator;

            internal DictionaryEnumerator(ConcurrentDictionaryLazy<TKey, TValue> dictionary) => this.enumerator = dictionary.GetEnumerator();

            public DictionaryEntry Entry
            {
                get
                {
                    var current = this.enumerator.Current;
                    var key = (object)current.Key;
                    current = this.enumerator.Current;
                    var local = (object)current.Value;
                    return new DictionaryEntry(key, local);
                }
            }

            public object Key => this.enumerator.Current.Key;

            public object Value => this.enumerator.Current.Value;

            public object Current => Entry;

            public bool MoveNext() => this.enumerator.MoveNext();

            public void Reset() => this.enumerator.Reset();
        }

        #endregion

        #region publics

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() =>
            this.dictionary.Select(kv => new KeyValuePair<TKey, TValue>(kv.Key, kv.Value.Value)).GetEnumerator();

        public void Clear() => this.dictionary.Clear();

        public bool Remove(TKey key) => this.dictionary.TryRemove(key, out _);

        public int Count => this.dictionary.Count;

        public bool IsEmpty => this.dictionary.IsEmpty;

        public bool ContainsKey(TKey key) => this.dictionary.ContainsKey(key);

        public TValue this[TKey key]
        {
            get => this.dictionary[key].Value;
            set => this.dictionary[key] = LazyValue(value);
        }

        public ICollection<TKey> Keys => this.dictionary.Keys;

        public ICollection<TValue> Values => new ReadOnlyCollection<TValue>(this.dictionary.Values.Select(s => s.Value).ToList());

        public KeyValuePair<TKey, TValue>[] ToArray() => this.dictionary.ToArray().Select(s => new KeyValuePair<TKey, TValue>(s.Key, s.Value.Value)).ToArray();

        #endregion

        #region Try

        static TValue GetLazyValue(Lazy<TValue> value) => value == null ? default : value.Value;

        public bool TryAdd(TKey key, TValue value) => this.dictionary.TryAdd(key, LazyValue(value));

        public bool TryAdd(TKey key, Func<TKey, TValue> valueFactory) =>
            valueFactory == null
                ? throw new ArgumentNullException(nameof(valueFactory))
                : this.dictionary.TryAdd(key, LazyValue(key, valueFactory));

        public bool TryGetValue(TKey key, out TValue value)
        {
            var result = this.dictionary.TryGetValue(key, out var lazy);
            value = GetLazyValue(lazy);
            return result;
        }

        public bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue) =>
            this.dictionary.TryGetValue(key, out var value)
            && EqualityComparer<TValue>.Default.Equals(value.Value, comparisonValue)
            && this.dictionary.TryUpdate(key, LazyValue(newValue), value);

        public bool TryUpdate(TKey key, Func<TKey, TValue> valueFactory, TValue comparisonValue) =>
            this.dictionary.TryGetValue(key, out var value)
            && EqualityComparer<TValue>.Default.Equals(value.Value, comparisonValue)
            && this.dictionary.TryUpdate(key, LazyValue(key, valueFactory), value);

        public bool TryRemove(TKey key, out TValue value)
        {
            var result = this.dictionary.TryRemove(key, out var lazy);
            value = GetLazyValue(lazy);
            return result;
        }

        #endregion

        #region GetOrAdd

        public TValue GetOrAdd(TKey key, TValue value) => this.dictionary.GetOrAdd(key, LazyValue(value)).Value;

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory) =>
            valueFactory == null
                ? throw new ArgumentNullException(nameof(valueFactory))
                : this.dictionary.GetOrAdd(key, LazyValue(valueFactory)).Value;

        #endregion

        #region AddOrUpdate

        public TValue AddOrUpdate(TKey key, TValue addValue, Func<TKey, TValue, TValue> updateValueFactory) =>
            this.dictionary.AddOrUpdate(key, LazyValue(addValue), LazyValue(updateValueFactory)).Value;

        public TValue AddOrUpdate(TKey key, Func<TKey, TValue> addValueFactory, Func<TKey, TValue, TValue> updateValueFactory)
        {
            return this.dictionary.AddOrUpdate(key, LazyValue(addValueFactory), LazyValue(updateValueFactory)).Value;
        }

        #endregion
    }
}