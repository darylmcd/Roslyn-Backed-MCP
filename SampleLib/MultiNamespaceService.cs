using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SampleLib;

/// <summary>
/// Sample service whose constructor takes parameters from three distinct BCL namespaces
/// (System.IO, System.Text, System.Collections.Generic). Used by the
/// scaffold-test-preview-missing-usings fixture to verify that scaffold_test_preview and
/// scaffold_first_test_file_preview emit using directives for all constructor-parameter
/// namespaces, not just the service type's own namespace.
/// </summary>
public class MultiNamespaceService
{
    private readonly TextWriter _writer;
    private readonly StringBuilder _buffer;
    private readonly IList<string> _items;

    public MultiNamespaceService(TextWriter writer, StringBuilder buffer, IList<string> items)
    {
        _writer = writer;
        _buffer = buffer;
        _items = items;
    }

    public void Flush()
    {
        _writer.Write(_buffer.ToString());
        _buffer.Clear();
    }

    public int Count() => _items.Count;
}
