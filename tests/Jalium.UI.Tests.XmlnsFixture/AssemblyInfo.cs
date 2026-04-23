using Jalium.UI.Markup;

// 这个程序集被 Jalium.UI.Tests 以 <ProjectReference> 形式引入,但测试代码**不 touch**
// 其中任何类型。用途:验证 XmlnsDefinitionRegistry.EnsureInitialized 的 force-load
// 路径能在 .NET lazy-load 行为下把 user assembly 拉进 AppDomain,从而扫到它上面声明的
// [XmlnsDefinition]。
[assembly: XmlnsDefinition("urn:test:jalium:xmlns-lazy-fixture", "Jalium.UI.Tests.XmlnsFixture")]
[assembly: XmlnsPrefix("urn:test:jalium:xmlns-lazy-fixture", "lazy")]
