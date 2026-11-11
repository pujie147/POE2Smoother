using System.Reflection;

namespace PsCore;

/// <summary>
/// 版本号唯一出口。值来自构建时的 Version 属性(git tag → CI → -p:Version),
/// 本地无 tag 构建拿到 <see cref="Fallback"/>。SDK 生成的 InformationalVersion 在默认设置下
/// 形如 "1.1.0+&lt;commit&gt;",这里截掉 '+' 之后的提交元数据,让 pscli version 和 GUI 标题
/// 显示同一个干净字符串。
/// </summary>
public static class BuildInfo
{
    public const string Fallback = "0.0.0-dev";

    /// <summary>去掉提交元数据的版本号,例如 1.1.0、0.0.1-rc1、0.0.0-dev。</summary>
    public static string Version { get; } = Resolve();

    private static string Resolve()
    {
        // GUI 和 CLI 都读自己的入口程序集;被别的宿主加载时回退到 PsCore 的版本
        // (同一次构建,值一样)。
        var assembly = Assembly.GetEntryAssembly() ?? typeof(BuildInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational)) return Fallback;
        return informational.Split('+', 2)[0].Trim();
    }
}
