using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Caseomatic.Net.Utility
{
    public class ConcurrentDictionary<TKey, TValue>
    {
        private readonly Dictionary<TKey, TValue> dictionary;
        private object lockObj;

        public TKey[] AllKeys
        {
            get
            {
                lock (lockObj)
                    return dictionary.Keys.ToArray();
            }
        }
        public TValue[] AllValues
        {
            get
            {
                lock (lockObj)
                    return dictionary.Values.ToArray();
            }
        }

        public int Count
        {
            get
            {
                lock (lockObj)
                    return dictionary.Count;
            }
        }

        public TValue this[TKey key]
        {
            get { return dictionary[key]; }
            set { dictionary[key] = value; }
        }

        public ConcurrentDictionary(int estimatedValues = 300)
        {
            dictionary = new Dictionary<TKey, TValue>(estimatedValues);
            lockObj = new object();
        }

        public void Add(TKey key, TValue value)
        {
            lock (lockObj)
            {
                dictionary.Add(key, value);
            }
        }
        public void AddRange(KeyValuePair<TKey, TValue>[] keyValuePairs)
        {
            lock (lockObj) // Or lock for each item individually and drop this lock-statement?
            {
                foreach (var keyValuePair in keyValuePairs)
                {
                    dictionary.Add(keyValuePair.Key, keyValuePair.Value);
                }
            }
        }

        public bool Remove(TKey key)
        {
            lock (lockObj)
                return dictionary.Remove(key);
        }

        public bool ContainsKey(TKey key)
        {
            lock (lockObj)
                return dictionary.ContainsKey(key);
        }

        public KeyValuePair<TKey, TValue>FirstOrDefault(Func<KeyValuePair<TKey, TValue>, bool> predicate)
        {
            lock (lockObj)
                return dictionary.FirstOrDefault(predicate);
        }

        public void Clear()
        {
            lock (lockObj)
                dictionary.Clear();
        }
    }
}
