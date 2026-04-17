using MathCursor.Core.Tokenization;
using Xunit;

namespace MathCursor.Core.Tests;

/// <summary>
/// Tests de smoke pour valider que le scaffold compile et les deps sont correctes.
/// Les vrais tests seront ajoutés en phase B quand les modules sont implémentés.
/// </summary>
public class ScaffoldTests
{
    [Fact]
    public void Tokenizer_EmptyInput_ReturnsEmptyList()
    {
        var result = Tokenizer.Tokenize("");
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
