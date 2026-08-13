using ScreenFind.Core.Models;

namespace ScreenFind.Core.Text;

/// <summary>
/// An <see cref="ExtractedDocument"/> plus its normalized form. Normalization of the page
/// happens once per capture; the query is normalized on every keystroke.
/// </summary>
public sealed class SearchableDocument
{
    public static readonly SearchableDocument Empty =
        new(ExtractedDocument.Empty, NormalizedText.Empty);

    private SearchableDocument(ExtractedDocument document, NormalizedText text)
    {
        Document = document;
        Text = text;
    }

    public ExtractedDocument Document { get; }

    public NormalizedText Text { get; }

    public bool IsEmpty => Document.IsEmpty || Text.IsEmpty;

    public static SearchableDocument Create(ExtractedDocument document)
        => new(document, TextNormalizer.Normalize(document.RawText));
}
