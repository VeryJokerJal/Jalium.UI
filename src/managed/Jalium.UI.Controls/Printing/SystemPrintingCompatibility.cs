namespace System.Printing;

/// <summary>
/// Provides the canonical WPF print-ticket identity while reusing Jalium's
/// cross-platform print settings implementation.
/// </summary>
public class PrintTicket : Jalium.UI.Controls.Printing.PrintTicket
{
    public PrintTicket()
    {
    }
}
