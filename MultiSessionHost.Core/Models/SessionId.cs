namespace MultiSessionHost.Core.Models;

public readonly record struct SessionId
{
    public SessionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("SessionId cannot be empty.", nameof(value));
        }

        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public static SessionId Parse(string value) => new(value);

    public static implicit operator string(SessionId sessionId) => sessionId.Value;

    public override string ToString() => Value;
}
