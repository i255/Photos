using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class Val<T> //where T : IEquatable<T>
    {
        //private T _lastFuncVal;
        private T _const;
        public Func<T> Func;
        public event Action<T,T> OnConstSet;

        public T Value
        {
            get
            {
                if (Func == null)
                    return _const;
                var val = Func();
                //var old = _lastFuncVal;
                //_lastFuncVal = val;

                //if (val == null && old != null || val != null && !val.Equals(old))
                //    OnChange?.Invoke(old, val);

                return val;
            }
        }

        public T Const
        {
            set
            {
                Func = null;
                if (_const != null && !_const.Equals(value) || _const == null && value != null)
                {
                    var old = _const;
                    _const = value;
                    OnConstSet?.Invoke(old, value);
                }
            }
        }

        public static implicit operator T(Val<T> v) => v.Value;

        public Val(T c) { _const = c; }
        public Val(Func<T> func) { Func = func; }

        public void CheckOnChange() => _ = Value;

        public void Adjust(Func<T, T> f)
        {
            var lastFunc = Func;

            if (lastFunc == null)
                Func = () => f(_const);
            else
                Func = () => f(lastFunc());
        }
    }
}
