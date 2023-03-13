using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dao.ConcurrentDictionaryLazy;
using Dao.IndividualLock;

namespace Dao.ConcurrentDictionaryLazyAsync
{
    [Serializable]
    public class ConcurrentDictionaryLazyAsync<TKey, TValue> : ConcurrentDictionaryLazy<TKey, TValue>
    {
        readonly IndividualLocks<TKey> asyncLock;

        #region Constructor

        public ConcurrentDictionaryLazyAsync()
            : this(null) { }

        public ConcurrentDictionaryLazyAsync(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer = null)
            : this(0, collection, comparer) { }

        public ConcurrentDictionaryLazyAsync(IEqualityComparer<TKey> comparer)
            : this(0, comparer: comparer) { }

        public ConcurrentDictionaryLazyAsync(int concurrencyLevel, IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer = null)
            : base(concurrencyLevel, collection, comparer)
        {
            this.asyncLock = new IndividualLocks<TKey>(this.comparer);
        }

        public ConcurrentDictionaryLazyAsync(int concurrencyLevel, int capacity = 31, IEqualityComparer<TKey> comparer = null)
            : base(concurrencyLevel, capacity, comparer)
        {
            this.asyncLock = new IndividualLocks<TKey>(this.comparer);
        }

        #endregion

        #region Async

        public async Task<bool> TryAddAsync(TKey key, Func<TKey, Task<TValue>> valueFactoryAsync)
        {
            if (valueFactoryAsync == null)
                throw new ArgumentNullException(nameof(valueFactoryAsync));

            if (this.dictionary.TryGetValue(key, out _))
                return false;

            using (await this.asyncLock.LockAsync(key).ConfigureAwait(false))
            {
                if (this.dictionary.TryGetValue(key, out _))
                    return false;

                var newValue = await valueFactoryAsync(key).ConfigureAwait(false);
                return this.dictionary.TryAdd(key, LazyValue(newValue));
            }
        }

        public async Task<bool> TryUpdateAsync(TKey key, Func<TKey, Task<TValue>> valueFactoryAsync, TValue comparisonValue)
        {
            if (valueFactoryAsync == null)
                throw new ArgumentNullException(nameof(valueFactoryAsync));

            if (!this.dictionary.TryGetValue(key, out var value)
                || !EqualityComparer<TValue>.Default.Equals(value.Value, comparisonValue))
                return false;

            using (await this.asyncLock.LockAsync(key).ConfigureAwait(false))
            {
                if (!this.dictionary.TryGetValue(key, out value)
                    || !EqualityComparer<TValue>.Default.Equals(value.Value, comparisonValue))
                    return false;

                var newValue = await valueFactoryAsync(key).ConfigureAwait(false);
                return this.dictionary.TryUpdate(key, LazyValue(newValue), value);
            }
        }

        public async Task<TValue> GetOrAddAsync(TKey key, Func<TKey, Task<TValue>> valueFactoryAsync)
        {
            if (valueFactoryAsync == null)
                throw new ArgumentNullException(nameof(valueFactoryAsync));

            if (this.dictionary.TryGetValue(key, out var value))
                return value.Value;

            using (await this.asyncLock.LockAsync(key).ConfigureAwait(false))
            {
                if (this.dictionary.TryGetValue(key, out value))
                    return value.Value;

                var newValue = await valueFactoryAsync(key).ConfigureAwait(false);
                return this.dictionary.GetOrAdd(key, LazyValue(newValue)).Value;
            }
        }

        public async Task<TValue> AddOrUpdateAsync(TKey key, TValue addValue, Func<TKey, TValue, Task<TValue>> updateValueFactoryAsync)
        {
            if (updateValueFactoryAsync == null)
                throw new ArgumentNullException(nameof(updateValueFactoryAsync));

            TValue newValue;
            using (await this.asyncLock.LockAsync(key).ConfigureAwait(false))
            {
                Lazy<TValue> comparisonValue;
                do
                {
                    while (!this.dictionary.TryGetValue(key, out comparisonValue))
                    {
                        if (this.dictionary.TryAdd(key, LazyValue(addValue)))
                            return addValue;
                    }

                    newValue = await updateValueFactoryAsync(key, comparisonValue.Value).ConfigureAwait(false);
                } while (!this.dictionary.TryUpdate(key, LazyValue(newValue), comparisonValue));
            }

            return newValue;
        }

        public async Task<TValue> AddOrUpdateAsync(TKey key, Func<TKey, Task<TValue>> addValueFactoryAsync, Func<TKey, TValue, Task<TValue>> updateValueFactoryAsync)
        {
            if (addValueFactoryAsync == null)
                throw new ArgumentNullException(nameof(addValueFactoryAsync));
            if (updateValueFactoryAsync == null)
                throw new ArgumentNullException(nameof(updateValueFactoryAsync));

            TValue newValue;
            using (await this.asyncLock.LockAsync(key).ConfigureAwait(false))
            {
                Lazy<TValue> comparisonValue;
                do
                {
                    while (!this.dictionary.TryGetValue(key, out comparisonValue))
                    {
                        var addValue = await addValueFactoryAsync(key).ConfigureAwait(false);
                        if (this.dictionary.TryAdd(key, LazyValue(addValue)))
                            return addValue;
                    }

                    newValue = await updateValueFactoryAsync(key, comparisonValue.Value).ConfigureAwait(false);
                } while (!this.dictionary.TryUpdate(key, LazyValue(newValue), comparisonValue));
            }

            return newValue;
        }

        #endregion
    }
}