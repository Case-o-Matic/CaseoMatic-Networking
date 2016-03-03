using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Caseomatic.Net.Utility
{
    public sealed class LineGraph : IEnumerable<float>
    {
        private readonly int estimatedValues;
        private readonly List<float> yValues;

        private bool clearOnMaxValue;
        public bool ClearOnMaxValue
        {
            get { return clearOnMaxValue; }
            set { clearOnMaxValue = value; }
        }

        public float AverageY
        {
            get
            {
                var count = yValues.Count;
                var sum = yValues.Aggregate((y, ySum) => ySum += y);

                return sum / count;
            }
        }

        public float this[int x]
        {
            get { return yValues[x]; }
            set { Add(x, value); }
        }
        public float this[float x]
        {
            get { return this[(int)Math.Round(x)]; }
            set { this[x] = value; }
        }

        public LineGraph(bool clearOnMaxValue, int estimatedValues = 200)
        {
            this.clearOnMaxValue = clearOnMaxValue;
            this.estimatedValues = estimatedValues;

            yValues = new List<float>(estimatedValues);
        }

        public void Add(float y)
        {
            yValues.Add(y);
        }

        public void Add(int x, float y)
        {
            yValues.Insert(x, y);
        }

        public void AddRange(float[] ys)
        {
            yValues.AddRange(ys);
        }

        public void AddRange(int x, float[] ys)
        {
            yValues.InsertRange(x, ys);
        }

        public void Clear()
        {
            yValues.Clear();
        }

        private void CheckClearOnMax()
        {
            if (yValues.Count >= estimatedValues)
            {
                Clear();
            }
        }

        public IEnumerator<float> GetEnumerator()
        {
            return yValues.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
