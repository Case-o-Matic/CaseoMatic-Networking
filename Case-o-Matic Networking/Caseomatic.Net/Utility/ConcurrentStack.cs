using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Caseomatic.Net.Utility
{
    public class ConcurrentStack<T>
    {
        private readonly Stack<T> stack;
        private object lockObj;

        public bool IsEmpty
        {
            get { return stack.Count == 0; }
        }

        public ConcurrentStack()
        {
            stack = new Stack<T>();
            lockObj = new object();
        }

        public void Push(T item)
        {
            lock (lockObj)
                stack.Push(item);
        }

        public void PushRange(T[] items)
        {
            lock (lockObj)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    stack.Push(items[i]);
                }
            }
        }

        public T Pop()
        {
            lock (lockObj)
                return stack.Pop();
        }

        public T[] PopAll()
        {
            lock (lockObj)
            {
                var items = stack.ToArray();
                stack.Clear();

                return items;
            }
        }

        public void Clear()
        {
            lock (lockObj)
            {
                stack.Clear();
            }
        }
    }
}
