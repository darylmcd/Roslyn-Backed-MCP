using System.Collections.Frozen;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Static, curated registry mapping known security-relevant diagnostic IDs to categories,
/// OWASP classifications, severity levels, and fix hints. This is a lookup table, not an analyzer.
/// </summary>
public static class SecurityDiagnosticRegistry
{
    /// <summary>
    /// Returns <see langword="true"/> if the diagnostic ID is recognized as security-relevant.
    /// </summary>
    public static bool IsSecurityDiagnostic(string diagnosticId) =>
        Entries.ContainsKey(diagnosticId);

    /// <summary>
    /// Returns the security info for a diagnostic ID, or <see langword="null"/> if not recognized.
    /// </summary>
    public static SecurityDiagnosticInfo? GetSecurityInfo(string diagnosticId) =>
        Entries.GetValueOrDefault(diagnosticId);

    /// <summary>
    /// Returns all registered security diagnostic entries.
    /// </summary>
    public static IReadOnlyDictionary<string, SecurityDiagnosticInfo> All => Entries;

    private static readonly FrozenDictionary<string, SecurityDiagnosticInfo> Entries =
        BuildRegistry().ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, SecurityDiagnosticInfo> BuildRegistry()
    {
        var map = new Dictionary<string, SecurityDiagnosticInfo>(StringComparer.OrdinalIgnoreCase);

        // ── Microsoft.CodeAnalysis.NetAnalyzers (ships with .NET SDK) ──

        Add(map, "CA2100", "SQL Injection (Review)", "Injection", "A03:2021 Injection", "High",
            "Review SQL command text for user input. Use parameterized queries.");
        Add(map, "CA2109", "Review Visible Event Handlers", "Access Control", "A01:2021 Broken Access Control", "Medium",
            "Ensure event handlers are not unintentionally public.");
        Add(map, "CA2119", "Seal Private Interface Methods", "Access Control", "A01:2021 Broken Access Control", "Medium",
            "Seal methods satisfying private interfaces to prevent external override.");
        Add(map, "CA2153", "Corrupted State Exceptions", "Error Handling", "A09:2021 Security Logging and Monitoring Failures", "High",
            "Do not catch corrupted state exceptions. Let the process terminate.");

        // Insecure deserialization (CA2300-CA2330)
        Add(map, "CA2300", "Insecure Deserializer – BinaryFormatter", "Deserialization", "A08:2021 Software and Data Integrity Failures", "Critical",
            "Do not use BinaryFormatter. Use System.Text.Json or a safe alternative.");
        Add(map, "CA2301", "BinaryFormatter Without Binder", "Deserialization", "A08:2021 Software and Data Integrity Failures", "Critical",
            "Do not call BinaryFormatter.Deserialize without a binder.");
        Add(map, "CA2302", "BinaryFormatter Binder Not Set Before Deserialize", "Deserialization", "A08:2021 Software and Data Integrity Failures", "Critical",
            "Ensure BinaryFormatter.Binder is set before calling Deserialize.");
        Add(map, "CA2305", "Insecure Deserializer – LosFormatter", "Deserialization", "A08:2021 Software and Data Integrity Failures", "Critical",
            "Do not use LosFormatter for deserialization.");
        Add(map, "CA2310", "Insecure Deserializer – NetDataContractSerializer", "Deserialization", "A08:2021 Software and Data Integrity Failures", "Critical",
            "Do not use NetDataContractSerializer. Use a safe alternative.");
        Add(map, "CA2311", "NetDataContractSerializer Without Binder", "Deserialization", "A08:2021 Software and Data Integrity Failures", "Critical",
            "Do not deserialize without setting Binder first.");
        Add(map, "CA2312", "NetDataContractSerializer Binder Not Set", "Deserialization", "A08:2021 Software and Data Integrity Failures", "Critical",
            "Ensure Binder is set before calling Deserialize.");
        Add(map, "CA2315", "Insecure Deserializer – ObjectStateFormatter", "Deserialization", "A08:2021 Software and Data Integrity Failures", "Critical",
            "Do not use ObjectStateFormatter for deserialization.");
        Add(map, "CA2321", "JavaScriptSerializer With SimpleTypeResolver", "Deserialization", "A08:2021 Software and Data Integrity Failures", "High",
            "Do not use JavaScriptSerializer with SimpleTypeResolver.");
        Add(map, "CA2322", "JavaScriptSerializer Initialized With SimpleTypeResolver", "Deserialization", "A08:2021 Software and Data Integrity Failures", "High",
            "Ensure JavaScriptSerializer is not initialized with SimpleTypeResolver.");
        Add(map, "CA2326", "TypeNameHandling Not None", "Deserialization", "A08:2021 Software and Data Integrity Failures", "High",
            "Do not use TypeNameHandling values other than None.");
        Add(map, "CA2327", "Insecure JsonSerializerSettings", "Deserialization", "A08:2021 Software and Data Integrity Failures", "High",
            "Do not use insecure JsonSerializerSettings.");
        Add(map, "CA2328", "Insecure JsonSerializerSettings Possible", "Deserialization", "A08:2021 Software and Data Integrity Failures", "High",
            "Ensure JsonSerializerSettings are configured securely.");
        Add(map, "CA2329", "JsonSerializer With Insecure Settings", "Deserialization", "A08:2021 Software and Data Integrity Failures", "High",
            "Do not use JsonSerializer with insecure settings.");
        Add(map, "CA2330", "JsonSerializer With Insecure Settings Possible", "Deserialization", "A08:2021 Software and Data Integrity Failures", "High",
            "Ensure JsonSerializer configuration is secure.");

        // Taint-flow analyzers (CA3001-CA3012)
        Add(map, "CA3001", "SQL Injection", "Injection", "A03:2021 Injection", "Critical",
            "Use parameterized queries or stored procedures instead of string concatenation.");
        Add(map, "CA3002", "Cross-Site Scripting (XSS)", "Injection", "A03:2021 Injection", "High",
            "Encode output before rendering. Use HtmlEncoder or framework-provided encoding.");
        Add(map, "CA3003", "File Path Injection", "Injection", "A03:2021 Injection", "High",
            "Validate and sanitize file paths. Use Path.GetFullPath with allow-list validation.");
        Add(map, "CA3004", "Information Disclosure", "Information Disclosure", "A01:2021 Broken Access Control", "Medium",
            "Do not expose sensitive information in error messages or responses.");
        Add(map, "CA3005", "LDAP Injection", "Injection", "A03:2021 Injection", "High",
            "Validate and escape LDAP filter inputs.");
        Add(map, "CA3006", "Process Command Injection", "Injection", "A03:2021 Injection", "Critical",
            "Do not pass user input directly to Process.Start. Use allow-lists.");
        Add(map, "CA3007", "Open Redirect", "Redirect", "A01:2021 Broken Access Control", "Medium",
            "Validate redirect URLs against an allow-list of trusted destinations.");
        Add(map, "CA3008", "XPath Injection", "Injection", "A03:2021 Injection", "High",
            "Use parameterized XPath queries or validate inputs.");
        Add(map, "CA3009", "XML Injection", "Injection", "A03:2021 Injection", "High",
            "Validate XML input and use secure XML writers.");
        Add(map, "CA3010", "XAML Injection", "Injection", "A03:2021 Injection", "High",
            "Do not load XAML from untrusted sources.");
        Add(map, "CA3011", "DLL Injection", "Injection", "A03:2021 Injection", "Critical",
            "Do not load assemblies from user-controlled paths.");
        Add(map, "CA3012", "Regex Injection", "Injection", "A03:2021 Injection", "Medium",
            "Validate regex patterns from user input. Set timeout on Regex.");

        // XML and schema processing
        Add(map, "CA3061", "Schema By URL", "XML Processing", "A05:2021 Security Misconfiguration", "Medium",
            "Do not add schema by URL. Use local schema files.");
        Add(map, "CA3075", "Insecure DTD Processing", "XML Processing", "A05:2021 Security Misconfiguration", "High",
            "Set DtdProcessing to Prohibit or configure secure XmlReaderSettings.");
        Add(map, "CA3076", "Insecure XSLT Script Processing", "XML Processing", "A05:2021 Security Misconfiguration", "High",
            "Disable script execution in XsltSettings.");
        Add(map, "CA3077", "Insecure Processing In API Design", "XML Processing", "A05:2021 Security Misconfiguration", "Medium",
            "Review API design for insecure XML processing defaults.");

        // Web security
        Add(map, "CA3147", "Missing ValidateAntiForgeryToken", "CSRF", "A01:2021 Broken Access Control", "High",
            "Add [ValidateAntiForgeryToken] attribute to POST/PUT/DELETE action methods.");

        // Cryptography
        Add(map, "CA5350", "Weak Cryptographic Algorithm (SHA1)", "Cryptography", "A02:2021 Cryptographic Failures", "Medium",
            "Use SHA256 or SHA512 instead of SHA1.");
        Add(map, "CA5351", "Broken Cryptographic Algorithm (DES/TripleDES)", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Use AES instead of DES or TripleDES.");

        // CA5358-CA5404: Various crypto, TLS, and certificate validation
        Add(map, "CA5358", "Unsafe Cipher Mode", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Do not use ECB or other unsafe cipher modes. Use CBC or GCM.");
        Add(map, "CA5359", "Do Not Disable Certificate Validation", "Cryptography", "A02:2021 Cryptographic Failures", "Critical",
            "Do not set ServerCertificateValidationCallback to always return true.");
        Add(map, "CA5360", "Do Not Call Dangerous Methods In Deserialization", "Deserialization", "A08:2021 Software and Data Integrity Failures", "High",
            "Do not call dangerous methods during deserialization.");
        Add(map, "CA5361", "Do Not Disable SChannel Strong Crypto", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Do not disable strong cryptography for SChannel.");
        Add(map, "CA5362", "Potential Reference Cycle In Deserialized Object Graph", "Deserialization", "A08:2021 Software and Data Integrity Failures", "Medium",
            "Review deserialized object graphs for reference cycles.");
        Add(map, "CA5363", "Do Not Disable Request Validation", "Input Validation", "A03:2021 Injection", "High",
            "Do not disable ASP.NET request validation.");
        Add(map, "CA5364", "Do Not Use Deprecated Security Protocols", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Use TLS 1.2 or later. Do not use SSL3, TLS 1.0, or TLS 1.1.");
        Add(map, "CA5365", "Do Not Disable HTTP Header Checking", "Input Validation", "A03:2021 Injection", "Medium",
            "Do not disable HTTP header checking.");
        Add(map, "CA5366", "Use XmlReader For DataSet Read Xml", "XML Processing", "A05:2021 Security Misconfiguration", "Medium",
            "Use XmlReader when calling DataSet.ReadXml.");
        Add(map, "CA5367", "Do Not Serialize Types With Pointer Fields", "Deserialization", "A08:2021 Software and Data Integrity Failures", "Medium",
            "Do not serialize types containing pointer fields.");
        Add(map, "CA5368", "Set ViewStateUserKey", "CSRF", "A01:2021 Broken Access Control", "Medium",
            "Set ViewStateUserKey in Page_Init to prevent CSRF attacks.");
        Add(map, "CA5369", "Use XmlReader For Deserialize", "XML Processing", "A05:2021 Security Misconfiguration", "Medium",
            "Use XmlReader when calling XmlSerializer.Deserialize.");
        Add(map, "CA5370", "Use XmlReader For Validating Reader", "XML Processing", "A05:2021 Security Misconfiguration", "Medium",
            "Use XmlReader for XmlValidatingReader.");
        Add(map, "CA5371", "Use XmlReader For Schema Read", "XML Processing", "A05:2021 Security Misconfiguration", "Medium",
            "Use XmlReader when calling XmlSchema.Read.");
        Add(map, "CA5372", "Use XmlReader For XPathDocument", "XML Processing", "A05:2021 Security Misconfiguration", "Medium",
            "Use XmlReader when constructing XPathDocument.");
        Add(map, "CA5373", "Do Not Use Obsolete Key Derivation Function", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Use Rfc2898DeriveBytes with SHA256 or higher.");
        Add(map, "CA5374", "Do Not Use XslTransform", "XML Processing", "A05:2021 Security Misconfiguration", "High",
            "Use XslCompiledTransform instead of XslTransform.");
        Add(map, "CA5375", "Do Not Use Account Shared Access Signature", "Access Control", "A01:2021 Broken Access Control", "Medium",
            "Use service SAS or user delegation SAS instead of account SAS.");
        Add(map, "CA5376", "Use SharedAccessProtocol HttpsOnly", "Cryptography", "A02:2021 Cryptographic Failures", "Medium",
            "Set SharedAccessProtocol to HttpsOnly.");
        Add(map, "CA5377", "Use Container Level Access Policy", "Access Control", "A01:2021 Broken Access Control", "Medium",
            "Use container-level access policies for SAS tokens.");
        Add(map, "CA5378", "Do Not Disable ServicePointManager SecurityProtocols", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Do not set SecurityProtocol to insecure values.");
        Add(map, "CA5379", "Do Not Use Weak Key Derivation Function Algorithm", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Use a strong hash algorithm for key derivation (SHA256+).");
        Add(map, "CA5380", "Do Not Add Certificates To Root Store", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Do not programmatically add certificates to the root store.");
        Add(map, "CA5381", "Do Not Install Certificates To Root Store", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Do not install certificates into the trusted root store.");
        Add(map, "CA5382", "Use Secure Cookies In ASP.NET Core", "Cookie Security", "A05:2021 Security Misconfiguration", "Medium",
            "Set CookieOptions.Secure to true.");
        Add(map, "CA5383", "Ensure Use Of Secure Cookies In ASP.NET Core", "Cookie Security", "A05:2021 Security Misconfiguration", "Medium",
            "Ensure cookies are sent only over HTTPS.");
        Add(map, "CA5384", "Do Not Use Digital Signature Algorithm (DSA)", "Cryptography", "A02:2021 Cryptographic Failures", "Medium",
            "Use RSA or ECDSA instead of DSA.");
        Add(map, "CA5385", "Use RSA With Sufficient Key Size", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Use RSA keys of at least 2048 bits.");
        Add(map, "CA5386", "Avoid Hardcoding SecurityProtocolType Value", "Cryptography", "A02:2021 Cryptographic Failures", "Medium",
            "Do not hardcode SecurityProtocolType. Let the OS choose the default.");
        Add(map, "CA5387", "Do Not Use Weak Key Derivation Function With Insufficient Iteration Count", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Use at least 100,000 iterations for PBKDF2.");
        Add(map, "CA5388", "Ensure Sufficient Iteration Count When Using Weak Key Derivation Function", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Ensure PBKDF2 iteration count is at least 100,000.");
        Add(map, "CA5389", "Do Not Add Archive Item Path To Target File System Path", "Injection", "A03:2021 Injection", "High",
            "Validate archive entry paths to prevent zip-slip attacks.");
        Add(map, "CA5390", "Do Not Hard-Code Encryption Key", "Cryptography", "A02:2021 Cryptographic Failures", "Critical",
            "Do not hardcode encryption keys. Use secure key storage.");
        Add(map, "CA5391", "Use Antiforgery Tokens In ASP.NET Core MVC Controllers", "CSRF", "A01:2021 Broken Access Control", "High",
            "Use [AutoValidateAntiforgeryToken] or [ValidateAntiForgeryToken] attributes.");
        Add(map, "CA5392", "Use DefaultDllImportSearchPaths For P/Invokes", "Injection", "A03:2021 Injection", "Medium",
            "Specify DefaultDllImportSearchPaths to prevent DLL search-order hijacking.");
        Add(map, "CA5393", "Do Not Use Unsafe DllImportSearchPath Value", "Injection", "A03:2021 Injection", "Medium",
            "Do not use DllImportSearchPath values that include user-writable directories.");
        Add(map, "CA5394", "Do Not Use Insecure Randomness", "Cryptography", "A02:2021 Cryptographic Failures", "Medium",
            "Use RandomNumberGenerator instead of System.Random for security-sensitive values.");
        Add(map, "CA5395", "Miss HttpVerb Attribute For Action Methods", "CSRF", "A01:2021 Broken Access Control", "Medium",
            "Add explicit [HttpGet], [HttpPost], etc. attributes to action methods.");
        Add(map, "CA5396", "Set HttpOnly To True For HttpCookie", "Cookie Security", "A05:2021 Security Misconfiguration", "Medium",
            "Set HttpOnly = true on cookies to prevent XSS-based cookie theft.");
        Add(map, "CA5397", "Do Not Use Deprecated SslProtocols Values", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Use SslProtocols.Tls12 or SslProtocols.Tls13.");
        Add(map, "CA5398", "Avoid Hardcoded SslProtocols Values", "Cryptography", "A02:2021 Cryptographic Failures", "Medium",
            "Avoid hardcoding SslProtocols values. Use SslProtocols.None to let the OS choose.");
        Add(map, "CA5399", "Definitely Disable HttpClient Certificate Revocation List Check", "Cryptography", "A02:2021 Cryptographic Failures", "Medium",
            "Enable certificate revocation list checking.");
        Add(map, "CA5400", "Ensure HttpClient Certificate Revocation List Check Not Disabled", "Cryptography", "A02:2021 Cryptographic Failures", "Medium",
            "Ensure certificate revocation list checking is enabled.");
        Add(map, "CA5401", "Do Not Use CreateEncryptor With Non-Default IV", "Cryptography", "A02:2021 Cryptographic Failures", "Medium",
            "Use a cryptographically random IV, not a hardcoded one.");
        Add(map, "CA5402", "Use CreateEncryptor With Default IV", "Cryptography", "A02:2021 Cryptographic Failures", "Medium",
            "Use the default IV generation rather than providing a custom one.");
        Add(map, "CA5403", "Do Not Hard-Code Certificate", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Do not embed certificates in source code. Use certificate stores.");
        Add(map, "CA5404", "Do Not Disable Token Validation Checks", "Authentication", "A07:2021 Identification and Authentication Failures", "Critical",
            "Do not disable JWT token validation checks.");

        // ── SecurityCodeScan (NuGet package) ──

        Add(map, "SCS0001", "Command Injection", "Injection", "A03:2021 Injection", "Critical",
            "Do not pass user input to Process.Start. Use allow-lists and parameterization.");
        Add(map, "SCS0002", "SQL Injection (LINQ)", "Injection", "A03:2021 Injection", "Critical",
            "Use parameterized queries or LINQ methods instead of string concatenation in SQL.");
        Add(map, "SCS0003", "XPath Injection", "Injection", "A03:2021 Injection", "High",
            "Use parameterized XPath or validate user inputs before building queries.");
        Add(map, "SCS0005", "Weak Random Number Generator", "Cryptography", "A02:2021 Cryptographic Failures", "Medium",
            "Use RandomNumberGenerator instead of System.Random for security-sensitive values.");
        Add(map, "SCS0006", "Weak Hashing Function", "Cryptography", "A02:2021 Cryptographic Failures", "Medium",
            "Use SHA-256 or stronger. Avoid MD5 and SHA-1 for security purposes.");
        Add(map, "SCS0007", "XML External Entity (XXE)", "XML Processing", "A05:2021 Security Misconfiguration", "High",
            "Disable external entity resolution in XML parsers.");
        Add(map, "SCS0008", "Cookie Without HttpOnly", "Cookie Security", "A05:2021 Security Misconfiguration", "Medium",
            "Set HttpOnly = true to prevent JavaScript access to cookies.");
        Add(map, "SCS0009", "Cookie Without Secure Flag", "Cookie Security", "A05:2021 Security Misconfiguration", "Medium",
            "Set Secure = true so cookies are only sent over HTTPS.");
        Add(map, "SCS0010", "Weak Cipher Algorithm", "Cryptography", "A02:2021 Cryptographic Failures", "High",
            "Use AES with an appropriate mode (CBC/GCM). Avoid DES and RC2.");
        Add(map, "SCS0011", "Unsafe CSRF Configuration", "CSRF", "A01:2021 Broken Access Control", "High",
            "Ensure anti-forgery tokens are validated on state-changing requests.");
        Add(map, "SCS0012", "Controller Without Authorization", "Access Control", "A01:2021 Broken Access Control", "High",
            "Add [Authorize] attribute to controllers or actions that require authentication.");
        Add(map, "SCS0013", "Potential Usage Of Weak Cipher Mode", "Cryptography", "A02:2021 Cryptographic Failures", "Medium",
            "Avoid ECB mode. Use CBC or GCM.");
        Add(map, "SCS0014", "SQL Injection (Entity Framework)", "Injection", "A03:2021 Injection", "Critical",
            "Use parameterized queries or LINQ instead of raw SQL with string concatenation.");
        Add(map, "SCS0015", "Hardcoded Password", "Credentials", "A07:2021 Identification and Authentication Failures", "Critical",
            "Move passwords to secure configuration or a secrets manager.");
        Add(map, "SCS0016", "CSRF Token Validation Missing", "CSRF", "A01:2021 Broken Access Control", "High",
            "Add [ValidateAntiForgeryToken] to state-changing actions.");
        Add(map, "SCS0017", "Request Validation Disabled", "Input Validation", "A03:2021 Injection", "High",
            "Do not disable ASP.NET request validation.");
        Add(map, "SCS0018", "Path Traversal", "Injection", "A03:2021 Injection", "High",
            "Validate file paths and canonicalize before use. Reject path traversal sequences.");
        Add(map, "SCS0019", "OutputCache Conflict", "Information Disclosure", "A01:2021 Broken Access Control", "Medium",
            "Do not use OutputCache on actions that require authorization.");
        Add(map, "SCS0020", "ORM Injection (NHibernate)", "Injection", "A03:2021 Injection", "High",
            "Use parameterized queries with NHibernate instead of string concatenation.");
        Add(map, "SCS0021", "Request Validation Disabled (MVC)", "Input Validation", "A03:2021 Injection", "High",
            "Do not use [ValidateInput(false)] on controller actions.");
        Add(map, "SCS0022", "Event Validation Disabled", "Input Validation", "A03:2021 Injection", "Medium",
            "Do not disable event validation in Web Forms.");
        Add(map, "SCS0023", "View State Not Encrypted", "Information Disclosure", "A02:2021 Cryptographic Failures", "Medium",
            "Enable ViewState encryption to protect against tampering.");
        Add(map, "SCS0024", "View State MAC Disabled", "Information Disclosure", "A02:2021 Cryptographic Failures", "High",
            "Enable ViewState MAC validation.");
        Add(map, "SCS0025", "SQL Injection (OleDb)", "Injection", "A03:2021 Injection", "Critical",
            "Use parameterized OleDbCommand instead of string concatenation.");
        Add(map, "SCS0026", "SQL Injection (ODBC)", "Injection", "A03:2021 Injection", "Critical",
            "Use parameterized OdbcCommand instead of string concatenation.");
        Add(map, "SCS0027", "Open Redirect", "Redirect", "A01:2021 Broken Access Control", "Medium",
            "Validate redirect URLs. Use Url.IsLocalUrl or an allow-list.");
        Add(map, "SCS0028", "Insecure Deserialization (TypeNameHandling)", "Deserialization", "A08:2021 Software and Data Integrity Failures", "Critical",
            "Do not use TypeNameHandling.All or Auto. Use TypeNameHandling.None.");
        Add(map, "SCS0029", "Cross-Site Scripting (XSS)", "Injection", "A03:2021 Injection", "High",
            "Encode output. Use @Html.Encode or framework-provided encoding.");
        Add(map, "SCS0030", "Request Validation Disabled (Attribute)", "Input Validation", "A03:2021 Injection", "High",
            "Do not disable request validation via attributes.");
        Add(map, "SCS0031", "LDAP Injection", "Injection", "A03:2021 Injection", "High",
            "Validate and escape LDAP distinguished name and search filter inputs.");
        Add(map, "SCS0032", "LDAP Injection (Path)", "Injection", "A03:2021 Injection", "High",
            "Validate LDAP paths before use.");
        Add(map, "SCS0033", "LDAP Injection (Filter)", "Injection", "A03:2021 Injection", "High",
            "Encode special characters in LDAP filter inputs.");
        Add(map, "SCS0034", "Password Complexity (ASP.NET Identity)", "Authentication", "A07:2021 Identification and Authentication Failures", "Medium",
            "Configure minimum password complexity requirements.");
        Add(map, "SCS0035", "SQL Injection (Entity Framework Core)", "Injection", "A03:2021 Injection", "Critical",
            "Use parameterized queries with EF Core. Avoid FromSqlRaw with string concatenation.");
        Add(map, "SCS0036", "SQL Injection (EnterpriseLibrary)", "Injection", "A03:2021 Injection", "Critical",
            "Use parameterized queries with Enterprise Library Data Access.");
        Add(map, "SCS0037", "Insecure Deserialization (Various)", "Deserialization", "A08:2021 Software and Data Integrity Failures", "Critical",
            "Avoid insecure deserialization. Validate types before deserializing.");
        Add(map, "SCS0038", "Path Traversal (Cookie)", "Injection", "A03:2021 Injection", "High",
            "Do not use cookie values in file paths without validation.");
        Add(map, "SCS0039", "Certificate Validation Disabled", "Cryptography", "A02:2021 Cryptographic Failures", "Critical",
            "Do not disable certificate validation. Always validate server certificates.");

        return map;
    }

    private static void Add(
        Dictionary<string, SecurityDiagnosticInfo> map,
        string id, string shortName, string category, string owaspCategory,
        string severity, string fixHint)
    {
        map[id] = new SecurityDiagnosticInfo(id, shortName, category, owaspCategory, severity, fixHint);
    }
}

/// <summary>
/// Metadata for a single security-relevant diagnostic ID.
/// </summary>
/// <param name="DiagnosticId">The diagnostic identifier (e.g., <c>CA3001</c>).</param>
/// <param name="ShortName">Human-readable short name (e.g., "SQL Injection").</param>
/// <param name="SecurityCategory">Security category (e.g., "Injection", "Cryptography").</param>
/// <param name="OwaspCategory">OWASP Top 10 category (e.g., "A03:2021 Injection").</param>
/// <param name="SecuritySeverity">Severity level: Critical, High, Medium, or Low.</param>
/// <param name="FixHint">Short actionable fix description.</param>
public sealed record SecurityDiagnosticInfo(
    string DiagnosticId,
    string ShortName,
    string SecurityCategory,
    string OwaspCategory,
    string SecuritySeverity,
    string FixHint);
