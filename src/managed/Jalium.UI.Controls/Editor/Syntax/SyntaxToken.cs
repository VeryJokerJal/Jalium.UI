namespace Jalium.UI.Controls.Editor;

/// <summary>
/// A syntax token: a span of text with a classification.
/// </summary>
public readonly record struct SyntaxToken(int StartOffset, int Length, TokenClassification Classification);
