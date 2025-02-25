using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Core
{
    public class ArrayDict<T,Q> where T : class
    {
        private readonly Func<T, Q> _key;
        public int Length => count;
        int highWaterMark, count;
        T[] _values;
        Dictionary<Q, int> _reverse;
        public ArrayDict(Func<T,Q> key)
        {
            _key = key;
            SetValues(null);
        }

        public void SetValues(T[] values)
        {
            if (values == null || values.Length == 0)
                values = new T[] { null };

            if (values[0] != null)
                throw new Exception("null pointer");

            _values = values;

            _reverse = new(_values.Length + 10);
            for (int i = 0; i < values.Length; i++)
                if (values[i] != null)
                    _reverse[_key(values[i])] = i;

            highWaterMark = 1;
            count = values.Length;
        }

        public T[] ToArray()
        {
            lock (_reverse)
            {
                var res = new T[count];
                Array.Copy(_values, res, res.Length);
                return res;
            }
        }

        public int GetIndex(T obj, bool noAdd = false)
        {
            var key = _key(obj);

            lock (_reverse)
            {
                if (_reverse.TryGetValue(key, out var idx))
                    return idx;
                else if (noAdd)
                    return -1;

                idx = highWaterMark;
                while (idx < _values.Length && _values[idx] != null)
                    idx++;
                highWaterMark = idx;

                if (_values.Length <= idx)
                    Array.Resize(ref _values, Math.Max(_values.Length * 2, 32));

                _values[idx] = obj;
                _reverse[key] = idx;

                count = Math.Max(idx + 1, count);

                return idx;
            }
        }

        public T GetValue(int idx) => _values[idx];

        public void Remove(int idx)
        {
            if (idx == 0)
                throw new Exception("invalid op");

            var key = _key(_values[idx]);
            lock (_reverse)
            {
                _values[idx] = null;
                _reverse.Remove(key);
                highWaterMark = Math.Min(highWaterMark, idx);
            }
        }
    }
}
