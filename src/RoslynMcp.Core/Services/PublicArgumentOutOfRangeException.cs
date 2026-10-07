namespace RoslynMcp.Core.Services;

/// <summary>
/// Carries a caller-safe range correction while preserving the BCL range-exception identity.
/// Messages follow the publication policy of <see cref="PublicArgumentException"/>.
/// </summary>
public sealed class PublicArgumentOutOfRangeException : ArgumentOutOfRangeException, IPublicMessageException
{
    public PublicArgumentOutOfRangeException(string publicMessage, string parameterName)
        : base(parameterName, publicMessage)
    {
        PublicMessage = publicMessage;
    }

    public string PublicMessage { get; }
}
