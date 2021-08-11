using System.Collections;

namespace NuGet
{
    internal class HashCodeCombiner
    {
        private long _combinedHash64 = 0x1505L;

        public int CombinedHash
        {
            get { return _combinedHash64.GetHashCode(); }
        }

        public void AddInt32(int i)
        {
            _combinedHash64 = ((_combinedHash64 << 5) + _combinedHash64) ^ i;
        }

        public void AddObject(object o)
        {
            int oHashCode = (o != null) ? o.GetHashCode() : 0;
            AddInt32(oHashCode);
        }
    }
}
