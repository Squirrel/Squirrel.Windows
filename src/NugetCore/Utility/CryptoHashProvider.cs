using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NuGet.Resources;

namespace NuGet
{
    public class CryptoHashProvider : IHashProvider
    {
        /// <summary>
        /// Server token used to represent that the hash being used is SHA 512
        /// </summary>
        private const string SHA512HashAlgorithm = "SHA512";

        /// <summary>
        /// Server token used to represent that the hash being used is SHA 256
        /// </summary>
        private const string SHA256HashAlgorithm = "SHA256";

        private readonly string _hashAlgorithm;

        public CryptoHashProvider()
            : this(null)
        {

        }

        public CryptoHashProvider(string hashAlgorithm)
        {
            if (String.IsNullOrEmpty(hashAlgorithm))
            {
                hashAlgorithm = SHA512HashAlgorithm;
            }
            else if (!hashAlgorithm.Equals(SHA512HashAlgorithm, StringComparison.OrdinalIgnoreCase) &&
                    !hashAlgorithm.Equals(SHA256HashAlgorithm, StringComparison.OrdinalIgnoreCase))
            {
                // Only support a vetted list of hash algorithms.
                throw new ArgumentException(String.Format(CultureInfo.CurrentCulture, NuGetResources.UnsupportedHashAlgorithm, hashAlgorithm), "hashAlgorithm");
            }

            _hashAlgorithm = hashAlgorithm;
        }

        /// <summary>
        /// Determines if we are to only allow Fips compliant algorithms. 
        /// </summary>
        /// <remarks>
        /// CryptoConfig.AllowOnlyFipsAlgorithm does not exist in Mono. 
        /// </remarks>
        private static bool AllowOnlyFipsAlgorithms
        {
            get
            {
                return ReadFipsConfigValue();
            }
        }

        public byte[] CalculateHash(Stream stream)
        {
            using (var hashAlgorithm = GetHashAlgorithm())
            {
                return hashAlgorithm.ComputeHash(stream);
            }
        }

        public byte[] CalculateHash(byte[] data)
        {
            using (var hashAlgorithm = GetHashAlgorithm())
            {
                return hashAlgorithm.ComputeHash(data);
            }
        }

        public bool VerifyHash(byte[] data, byte[] hash)
        {
            byte[] dataHash = CalculateHash(data);
            return Enumerable.SequenceEqual(dataHash, hash);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "We want to return the object.")]
        private HashAlgorithm GetHashAlgorithm()
        {
            if (_hashAlgorithm.Equals(SHA256HashAlgorithm, StringComparison.OrdinalIgnoreCase))
            {
                return AllowOnlyFipsAlgorithms ? (HashAlgorithm)new SHA256CryptoServiceProvider() : (HashAlgorithm)new SHA256Managed();
            }
            return AllowOnlyFipsAlgorithms ? (HashAlgorithm)new SHA512CryptoServiceProvider() : (HashAlgorithm)new SHA512Managed();
        }

        private static bool ReadFipsConfigValue()
        {
            // Mono does not currently support this method. Have this in a separate method to avoid JITing exceptions.
            var cryptoConfig = typeof(System.Security.Cryptography.CryptoConfig);

            if (cryptoConfig != null)
            {
                var allowOnlyFipsAlgorithmsProperty = cryptoConfig.GetProperty("AllowOnlyFipsAlgorithms", BindingFlags.Public | BindingFlags.Static);

                if (allowOnlyFipsAlgorithmsProperty != null)
                {
                    return (bool)allowOnlyFipsAlgorithmsProperty.GetValue(null, null);
                }
            }

            return false;
        }
    }
}
