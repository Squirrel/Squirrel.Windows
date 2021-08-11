using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet
{
    public class SettingValue
    {
        public SettingValue(string key, string value, bool isMachineWide, int priority = 0)
        {
            Key = key;
            Value = value;
            IsMachineWide = isMachineWide;
            Priority = priority;
        }

        public string Key { get; private set; }

        public string Value { get; private set; }

        public bool IsMachineWide { get; private set; }

        // The priority of this setting in the nuget.config hierarchy. Bigger number means higher priority.
        public int Priority { get; private set; }

        public override bool Equals(object obj)
        {
            var rhs = obj as SettingValue;
            if (rhs == null)
            {
                return false;
            }

            return rhs.Key == Key && 
                rhs.Value == Value && 
                rhs.IsMachineWide == rhs.IsMachineWide;
        }

        public override int GetHashCode()
        {
            return Tuple.Create(Key, Value, IsMachineWide).GetHashCode();
        }
    }
}
