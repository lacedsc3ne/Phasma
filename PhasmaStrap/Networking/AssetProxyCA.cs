using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PhasmaStrap.Networking
{
    // generates and manages the local root certificate authority PhasmaStrap uses to
    // terminate TLS for the specific Roblox API hosts it intercepts. The CA is generated
    // once and stored under LocalAppData; it is never installed into the trust store
    // automatically - that's a separate, explicitly user-triggered action, since it's a
    // system trust-boundary change that affects every app on the machine, not just this one.
    public static class AssetProxyCA
    {
        private const string LOG_IDENT = "AssetProxyCA";

        private const string SubjectName = "CN=PhasmaStrap Local Proxy CA";

        private static string CertificateFile => Path.Combine(Paths.LocalAppData, "PhasmaStrap", "AssetProxy", "ca.pfx");

        private static X509Certificate2? _cached;

        private static readonly Dictionary<string, X509Certificate2> LeafCache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object Sync = new();

        public static X509Certificate2 GetOrCreateRootCertificate()
        {
            lock (Sync)
            {
                if (_cached is not null)
                    return _cached;

                string path = CertificateFile;

                if (File.Exists(path))
                {
                    try
                    {
                        var existing = new X509Certificate2(path, (string?)null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                        if (existing.NotAfter > DateTime.Now.AddDays(7))
                        {
                            _cached = existing;
                            return existing;
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger.WriteLine(LOG_IDENT, $"Existing CA could not be loaded, regenerating: {ex.Message}");
                    }
                }

                var created = CreateRootCertificate();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, created.Export(X509ContentType.Pfx));
                App.Logger.WriteLine(LOG_IDENT, $"Generated new local proxy CA, valid until {created.NotAfter}");

                _cached = created;
                return created;
            }
        }

        private static X509Certificate2 CreateRootCertificate()
        {
            using RSA rsa = RSA.Create(2048);

            var request = new CertificateRequest(SubjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            X509Certificate2 cert = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(2));

            // re-import as exportable so it can be persisted and used to sign leaf certs later
            return new X509Certificate2(cert.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }

        // generates (or returns a cached) leaf certificate for a given hostname, signed by
        // the local root CA, for use when terminating TLS for that specific host
        public static X509Certificate2 GetLeafCertificate(string hostname)
        {
            lock (Sync)
            {
                if (LeafCache.TryGetValue(hostname, out var existing) && existing.NotAfter > DateTime.Now.AddDays(1))
                    return existing;

                X509Certificate2 root = GetOrCreateRootCertificate();

                using RSA rsa = RSA.Create(2048);
                var request = new CertificateRequest($"CN={hostname}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                var sanBuilder = new SubjectAlternativeNameBuilder();
                sanBuilder.AddDnsName(hostname);
                request.CertificateExtensions.Add(sanBuilder.Build());
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

                byte[] serial = RandomNumberGenerator.GetBytes(16);
                X509Certificate2 leaf = request.Create(root, DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1), serial);
                X509Certificate2 leafWithKey = leaf.CopyWithPrivateKey(rsa);

                var exportable = new X509Certificate2(leafWithKey.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                LeafCache[hostname] = exportable;
                return exportable;
            }
        }

        public static bool IsInstalledInTrustStore()
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            X509Certificate2 root = GetOrCreateRootCertificate();
            return store.Certificates.Find(X509FindType.FindByThumbprint, root.Thumbprint, false).Count > 0;
        }

        // installs the root CA into the CURRENT USER'S trust store only (not machine-wide,
        // so it doesn't require administrator rights) - only call this from an explicit,
        // clearly-labelled user action, never automatically
        public static bool InstallToTrustStore()
        {
            try
            {
                X509Certificate2 root = GetOrCreateRootCertificate();
                using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);
                store.Add(root);
                App.Logger.WriteLine(LOG_IDENT, "Root CA installed to CurrentUser trust store");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }

        public static bool RemoveFromTrustStore()
        {
            try
            {
                X509Certificate2 root = GetOrCreateRootCertificate();
                using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);
                store.Remove(root);
                App.Logger.WriteLine(LOG_IDENT, "Root CA removed from CurrentUser trust store");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.WriteException(LOG_IDENT, ex);
                return false;
            }
        }
    }
}
