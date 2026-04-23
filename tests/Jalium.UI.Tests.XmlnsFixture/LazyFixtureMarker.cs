namespace Jalium.UI.Tests.XmlnsFixture;

/// <summary>
/// 占位类型,确保程序集至少有一个公有类型可被 XmlnsDefinition 映射到的 CLR namespace 找到。
/// 测试代码**不直接引用**这个类型,它完全靠 force-load 机制被发现。
/// </summary>
public sealed class LazyFixtureMarker;
