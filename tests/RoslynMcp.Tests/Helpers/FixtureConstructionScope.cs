namespace RoslynMcp.Tests.Helpers;

internal static class FixtureConstructionScope
{
    internal static T Create<T>(string root, Func<T> construct, Action<string> deleteRoot)
    {
        Directory.CreateDirectory(root);
        try
        {
            return construct();
        }
        catch (Exception constructionFailure)
        {
            try
            {
                deleteRoot(root);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Fixture construction and owned-root cleanup both failed.",
                    constructionFailure, cleanupFailure);
            }

            throw;
        }
    }
}
