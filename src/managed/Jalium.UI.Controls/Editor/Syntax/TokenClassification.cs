namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Classification of a syntax token for highlighting.
/// </summary>
public enum TokenClassification
{
    PlainText,
    Keyword,
    ControlKeyword,
    TypeName,
    String,
    Character,
    Number,
    Comment,
    XmlDoc,
    Preprocessor,
    Operator,
    Punctuation,
    Identifier,
    LocalVariable,
    Parameter,
    Field,
    Property,
    Method,
    Namespace,
    Attribute,
    BindingKeyword,
    BindingParameter,
    BindingPath,
    BindingOperator,
    Error
}
