namespace RoslynMcp.Core.Services;

/// <summary>
/// Carries a caller-safe range correction while preserving the BCL range-exception identity.
/// Messages follow the publication policy of <see cref="PublicArgumentException"/>.
/// </summary>
public sealed class PublicArgumentOutOfRangeException : ArgumentOutOfRangeException, IPublicMessageException
{
#pragma warning disable RS0030 // The sanctioned public out-of-range carrier constructs its base type (src/BannedSymbols.txt).
    public PublicArgumentOutOfRangeException(string publicMessage, string parameterName)
        : base(parameterName, publicMessage)
#pragma warning restore RS0030
    {
        PublicMessage = publicMessage;
    }

    public string PublicMessage { get; }
}
