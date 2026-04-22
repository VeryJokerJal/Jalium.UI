using System;
using System.Collections.Generic;
using System.Text;

namespace Jalium.UI.Markup
{

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class XmlnsDefinitionAttribute : Attribute
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="xmlNamespace">
        /// XmlNamespace used by Markup file
        /// </param>
        /// <param name="clrNamespace">
        /// Clr namespace which contains known types that are used by Markup File.
        /// </param>
        public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace)
        {
            XmlNamespace = xmlNamespace ?? throw new ArgumentNullException(nameof(xmlNamespace));
            ClrNamespace = clrNamespace ?? throw new ArgumentNullException(nameof(clrNamespace));
        }

        /// <summary>
        /// XmlNamespace which can be used in Markup file.
        /// such as XmlNamespace is set to
        /// "http://schemas.fabrikam.com/mynamespace".
        ///
        /// The markup file can have definition like
        /// xmlns:myns="http://schemas.fabrikam.com/mynamespace"
        /// </summary>
        public string XmlNamespace { get; }

        /// <summary>
        /// ClrNamespace which map to XmlNamespace.
        /// This ClrNamespace should contain some types which are used
        /// by Xaml markup file.
        /// </summary>
        public string ClrNamespace { get; }

        /// <summary>
        /// The name of Assembly that contains some types inside CLRNamespace.
        /// If the assemblyName is not set, the code should take the assembly
        /// for which the instance of this attribute is created.
        /// </summary>
        public string? AssemblyName { get; set; }
    }
}
