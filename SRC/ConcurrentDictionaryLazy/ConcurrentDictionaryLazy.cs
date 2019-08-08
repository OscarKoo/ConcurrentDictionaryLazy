﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ConcurrentDictionaryLazy
{
    public class ConcurrentDictionaryLazy<TKey, TValue> : IDictionary<TKey, TValue>
    {
        static readonly int processCount = Environment.ProcessorCount;
        static readonly IEqualityComparer<TKey> defaultComparer = EqualityComparer<TKey>.Default;

        readonly ConcurrentDictionary<TKey, Lazy<TValue>> dictionary;

        #region Constructor

        public ConcurrentDictionaryLazy()
        {
            this.dictionary = new ConcurrentDictionary<TKey, Lazy<TValue>>();
        }

        public ConcurrentDictionaryLazy(IEqualityComparer<TKey> comparer)
        {
            this.dictionary = new ConcurrentDictionary<TKey, Lazy<TValue>>(comparer);
        }

        public ConcurrentDictionaryLazy(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer = null)
        {
            this.dictionary = new ConcurrentDictionary<TKey, Lazy<TValue>>(ConvertToLazy(collection), comparer ?? defaultComparer);
        }

        public ConcurrentDictionaryLazy(int concurrencyLevel, IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer = null)
        {
            this.dictionary = new ConcurrentDictionary<TKey, Lazy<TValue>>(concurrencyLevel, ConvertToLazy(collection), comparer ?? defaultComparer);
        }

        public ConcurrentDictionaryLazy(int concurrencyLevel, int capacity = 31, IEqualityComparer<TKey> comparer = null)
        {
            this.dictionary = new ConcurrentDictionary<TKey, Lazy<TValue>>(concurrencyLevel <= 0 ? processCount : concurrencyLevel, capacity, comparer ?? defaultComparer);
        }

        static IEnumerable<KeyValuePair<TKey, Lazy<TValue>>> ConvertToLazy(IEnumerable<KeyValuePair<TKey, TValue>> collection)
        {
            return collection.Select(s => new KeyValuePair<TKey, Lazy<TValue>>(s.Key, NewValue(s.Value)));
        }

        #endregion

        static Lazy<TValue> NewValue(TValue value)
        {
            return new Lazy<TValue>(() => value);
        }

        static Lazy<TValue> NewValue(TKey key, Func<TKey, TValue> addValueFactory)
        {
            if (addValueFactory == null)
                throw new ArgumentNullException(nameof(addValueFactory));

            return new Lazy<TValue>(() => addValueFactory(key));
        }

        static Lazy<TValue> NewValue(TKey key, Lazy<TValue> value, Func<TKey, TValue, TValue> updateValueFactory)
        {
            if (updateValueFactory == null)
                throw new ArgumentNullException(nameof(updateValueFactory));

            return new Lazy<TValue>(() => updateValueFactory(key, value.Value));
        }

        static Lazy<TValue> NewValue(TKey key, Lazy<TValue> value, Action<TKey, TValue> updateValueFactory)
        {
            if (updateValueFactory == null)
                throw new ArgumentNullException(nameof(updateValueFactory));

            updateValueFactory(key, value.Value);
            return value;
        }

        static TValue CheckValue(Lazy<TValue> value)
        {
            return value == null ? default(TValue) : value.Value;
        }

        #region IDictionary

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            foreach (var kv in this.dictionary)
            {
                yield return new KeyValuePair<TKey, TValue>(kv.Key, kv.Value.Value);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            ((IDictionary<TKey, TValue>)this).Add(item.Key, item.Value);
        }

        public void Clear()
        {
            this.dictionary.Clear();
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            return TryGetValue(item.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            var items = new KeyValuePair<TKey, Lazy<TValue>>[array.Length];
            ((ICollection<KeyValuePair<TKey, Lazy<TValue>>>)this.dictionary).CopyTo(items, arrayIndex);
            for (var i = arrayIndex; i < items.Length; i++)
            {
                var current = items[i];
                array[i] = new KeyValuePair<TKey, TValue>(current.Key, current.Value.Value);
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            return ((IDictionary<TKey, TValue>)this).Remove(item.Key);
        }

        public int Count => this.dictionary.Count;

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => ((ICollection<KeyValuePair<TKey, Lazy<TValue>>>)this.dictionary).IsReadOnly;

        void IDictionary<TKey, TValue>.Add(TKey key, TValue value)
        {
            ((IDictionary<TKey, Lazy<TValue>>)this.dictionary).Add(key, NewValue(value));
        }

        public bool ContainsKey(TKey key)
        {
            return this.dictionary.ContainsKey(key);
        }

        public bool Remove(TKey key)
        {
            return ((IDictionary<TKey, Lazy<TValue>>)this.dictionary).Remove(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            var result = this.dictionary.TryGetValue(key, out var lazy);
            value = CheckValue(lazy);
            return result;
        }

        public TValue this[TKey key]
        {
            get => CheckValue(this.dictionary[key]);
            set => this.dictionary[key] = NewValue(value);
        }

        public ICollection<TKey> Keys => this.dictionary.Keys;

        public ICollection<TValue> Values => new ReadOnlyCollection<TValue>(this.dictionary.Values.Select(s => s.Value).ToList());

        #endregion

        public bool TryAdd(TKey key, TValue value)
        {
            return this.dictionary.TryAdd(key, NewValue(value));
        }

        public bool TryAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            return this.dictionary.TryAdd(key, NewValue(key, valueFactory));
        }

        /// <summary>
        /// Not thread-safe
        /// </summary>
        [Obsolete]
        public bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue)
        {
            if (!this.dictionary.TryGetValue(key, out var value)
                || !EqualityComparer<TValue>.Default.Equals(value.Value, comparisonValue))
                return false;

            this.dictionary[key] = NewValue(newValue);
            return true;
        }

        /// <summary>
        /// Not thread-safe
        /// </summary>
        [Obsolete]
        public bool TryUpdate(TKey key, Func<TKey, TValue> newValueFactory, TValue comparisonValue)
        {
            if (!this.dictionary.TryGetValue(key, out var value)
                || !EqualityComparer<TValue>.Default.Equals(value.Value, comparisonValue))
                return false;

            this.dictionary[key] = NewValue(key, newValueFactory);
            return true;
        }

        public bool TryRemove(TKey key, out TValue value)
        {
            var result = this.dictionary.TryRemove(key, out var lazy);
            value = CheckValue(lazy);
            return result;
        }

        public TValue GetOrAdd(TKey key, TValue value)
        {
            return this.dictionary.GetOrAdd(key, NewValue(value)).Value;
        }

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            return this.dictionary.GetOrAdd(key, k => NewValue(k, valueFactory)).Value;
        }

        /// <summary>
        /// Update will replace existing value
        /// </summary>
        public TValue AddOrUpdate(TKey key, TValue addValue, Func<TKey, TValue, TValue> updateValueFactory)
        {
            return this.dictionary.AddOrUpdate(key, NewValue(addValue), (k, v) => NewValue(k, v, updateValueFactory)).Value;
        }

        /// <summary>
        /// Update will replace existing value
        /// </summary>
        public TValue AddOrUpdate(TKey key, Func<TKey, TValue> addValueFactory, Func<TKey, TValue, TValue> updateValueFactory)
        {
            return this.dictionary.AddOrUpdate(key, NewValue(key, addValueFactory), (k, v) => NewValue(k, v, updateValueFactory)).Value;
        }

        /// <summary>
        /// Update will update existing reference type value
        /// </summary>
        public TValue AddOrUpdate(TKey key, TValue addValue, Action<TKey, TValue> updateValueFactory)
        {
            return this.dictionary.AddOrUpdate(key, NewValue(addValue), (k, v) => NewValue(k, v, updateValueFactory)).Value;
        }

        /// <summary>
        /// Update will update existing reference type value
        /// </summary>
        public TValue AddOrUpdate(TKey key, Func<TKey, TValue> addValueFactory, Action<TKey, TValue> updateValueFactory)
        {
            return this.dictionary.AddOrUpdate(key, NewValue(key, addValueFactory), (k, v) => NewValue(k, v, updateValueFactory)).Value;
        }

        public KeyValuePair<TKey, TValue>[] ToArray()
        {
            return this.dictionary.ToArray().Select(s => new KeyValuePair<TKey, TValue>(s.Key, s.Value.Value)).ToArray();
        }
    }
}