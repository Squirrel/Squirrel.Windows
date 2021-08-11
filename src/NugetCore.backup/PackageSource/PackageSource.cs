using System;
using System.Runtime.Serialization;

namespace NuGet
{
    [DataContract]
    public class PackageSource : IEquatable<PackageSource>
    {
        private readonly int _hashCode;

        [DataMember]
        public string Name { get; private set; }

        [DataMember]
        public string Source { get; private set; }

        /// <summary>
        /// This does not represent just the NuGet Official Feed alone
        /// It may also represent a Default Package Source set by Configuration Defaults
        /// </summary>
        public bool IsOfficial { get; set; }

        public bool IsMachineWide { get; set; }

        public bool IsEnabled { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public bool IsPasswordClearText { get; set; }

        public bool IsPersistable
        {
            get;
            private set;
        }

        public PackageSource(string source) :
            this(source, source, isEnabled: true)
        {
        }

        public PackageSource(string source, string name) :
            this(source, name, isEnabled: true)
        {
        }

        public PackageSource(string source, string name, bool isEnabled)
            : this(source, name, isEnabled, isOfficial: false)
        {
        }

        public PackageSource(
            string source, 
            string name, 
            bool isEnabled, 
            bool isOfficial,
            bool isPersistable = true)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            if (name == null)
            {
                throw new ArgumentNullException("name");
            }

            Name = name;
            Source = source;
            IsEnabled = isEnabled;
            IsOfficial = isOfficial;
            IsPersistable = isPersistable;
            _hashCode = Name.ToUpperInvariant().GetHashCode() * 3137 + Source.ToUpperInvariant().GetHashCode();
        }

        public bool Equals(PackageSource other)
        {
            if (other == null)
            {
                return false;
            }

            return Name.Equals(other.Name, StringComparison.CurrentCultureIgnoreCase) &&
                Source.Equals(other.Source, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            var source = obj as PackageSource;
            if (source != null)
            {
                return Equals(source);
            }
            return base.Equals(obj);
        }

        public override string ToString()
        {
            return Name + " [" + Source + "]";
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public PackageSource Clone()
        {
            return new PackageSource(Source, Name, IsEnabled, IsOfficial, IsPersistable) { UserName = UserName, Password = Password, IsPasswordClearText = IsPasswordClearText, IsMachineWide = IsMachineWide };
        }
    }
}