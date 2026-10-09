namespace RoslynMcp.Core.Services;

/// <summary>Creates operation failures whose diagnostic detail must stay in server logs.</summary>
public static class InvalidOperationErrors
{
    /// <summary>Preserves server detail and the underlying failure without marking them public.</summary>
    public static InvalidOperationException Internal(string serverDetail, Exception? inner = null)
        => new InvalidOperationException(serverDetail, inner);
}
