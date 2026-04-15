using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record ExecutionResourceKey
{
    public ExecutionResourceKey(ExecutionScope scope, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Execution resource key value cannot be empty.", nameof(value));
        }

        Scope = scope;
        Value = value.Trim();
    }

    public ExecutionScope Scope { get; }

    public string Value { get; }

    public static ExecutionResourceKey ForSession(SessionId sessionId) =>
        new(ExecutionScope.Session, $"session:{sessionId.Value}");

    public static ExecutionResourceKey ForTarget(string targetKey) =>
        new(ExecutionScope.Target, targetKey);

    public static ExecutionResourceKey ForGlobal(string globalKey) =>
        new(ExecutionScope.Global, globalKey);

    public override string ToString() => Value;
}
